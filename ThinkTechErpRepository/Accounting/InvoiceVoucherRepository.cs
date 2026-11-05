using Dapper;
using ThinkTechErpDomain.Models.Accounting;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Accounting;

/// <summary>The ledger mappings a Bill Voucher is worked out from when an invoice is finalized.</summary>
public interface IInvoiceVoucherRepository
{
    /// <summary>The products' categories and their ledger heads (products of other organizations are left out).</summary>
    Task<IReadOnlyList<ProductLedgerHead>> GetProductHeadsAsync(
        int organizationId, IReadOnlyCollection<int> productIds, CancellationToken cancellationToken = default);
    /// <summary>A supplier's or customer's ledger head.</summary>
    Task<PartyLedgerHead?> GetPartyHeadAsync(int organizationId, int partyId, CancellationToken cancellationToken = default);
    /// <summary>A payment source line (inv_payment_source_d) and its ledger head.</summary>
    Task<PaymentSourceLedgerHead?> GetPaymentSourceHeadAsync(int lineId, CancellationToken cancellationToken = default);
    Task<bool> LedgerHeadExistsAsync(int accSubClassId, CancellationToken cancellationToken = default);
    /// <summary>Sets just a supplier's or customer's ledger head (supplier.acc_sub_class_id); false if there's no such party.</summary>
    Task<bool> SetPartyLedgerHeadAsync(int organizationId, int partyId, int? accSubClassId, int userId, CancellationToken cancellationToken = default);
}

public sealed class InvoiceVoucherRepository(IDbConnectionFactory connectionFactory) : IInvoiceVoucherRepository
{
    public async Task<IReadOnlyList<ProductLedgerHead>> GetProductHeadsAsync(
        int organizationId, IReadOnlyCollection<int> productIds, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<ProductLedgerHead>(new CommandDefinition("""
            SELECT p.product_id AS ProductId, p.product_name AS ProductName, c.category_id AS CategoryId,
                   c.category_name AS CategoryName, c.acc_sub_class_id AS AccSubClassId
            FROM public.product p
            LEFT JOIN public.category c ON c.category_id = p.category_id
            WHERE p.organization_id = @Org AND p.product_id = ANY(@Ids)
            """, new { Org = organizationId, Ids = productIds.ToArray() }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<PartyLedgerHead?> GetPartyHeadAsync(int organizationId, int partyId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<PartyLedgerHead>(new CommandDefinition("""
            SELECT supplier_id AS Id, supplier_name AS Name, acc_sub_class_id AS AccSubClassId
            FROM public.supplier
            WHERE organization_id = @Org AND supplier_id = @Id
            """, new { Org = organizationId, Id = partyId }, cancellationToken: cancellationToken));
    }

    public async Task<PaymentSourceLedgerHead?> GetPaymentSourceHeadAsync(int lineId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<PaymentSourceLedgerHead>(new CommandDefinition("""
            SELECT d.id AS Id, h.source_name_en AS SourceName, d.payment_for AS PaymentFor, d.acc_sub_class_id AS AccSubClassId
            FROM public.inv_payment_source_d d
            JOIN public.inv_payment_source_h h ON h.id = d.inv_payment_source_h_id
            WHERE d.id = @Id
            """, new { Id = lineId }, cancellationToken: cancellationToken));
    }

    public async Task<bool> LedgerHeadExistsAsync(int accSubClassId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM public.acc_sub_class WHERE acc_sub_class_id = @Id)",
            new { Id = accSubClassId }, cancellationToken: cancellationToken));
    }

    public async Task<bool> SetPartyLedgerHeadAsync(
        int organizationId, int partyId, int? accSubClassId, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.supplier
            SET acc_sub_class_id = @AccSubClassId, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE organization_id = @Org AND supplier_id = @Id
            """, new { Org = organizationId, Id = partyId, AccSubClassId = accSubClassId, UserId = userId },
            cancellationToken: cancellationToken)) > 0;
    }
}
