using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Accounting;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Accounting;

public interface IVoucherRepository
{
    Task<PagedResult<Voucher>> GetVouchersAsync(int organizationId, VoucherQuery query, CancellationToken cancellationToken = default);
    Task<Voucher?> GetVoucherAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    /// <summary>
    /// Creates (Id 0, numbered from the type's series) or replaces a draft voucher with its lines; with post it is
    /// posted in the same transaction. Returns its id.
    /// </summary>
    Task<long> SaveVoucherAsync(int organizationId, Voucher voucher, bool post, int userId, CancellationToken cancellationToken = default);
    /// <summary>Drafts only; a posted or cancelled voucher answers InUse.</summary>
    Task<DeleteOutcome> DeleteVoucherAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task PostVoucherAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task CancelVoucherAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
}

public sealed class VoucherRepository(IDbConnectionFactory connectionFactory) : IVoucherRepository
{
    private const string VoucherSelect = """
        SELECT v.voucher_id AS Id, v.object_id AS ObjectId, v.voucher_no AS VoucherNo, v.voucher_type AS VoucherType,
               v.voucher_date AS VoucherDate, v.supplier_id AS SupplierId, s.supplier_code AS SupplierCode,
               s.supplier_name AS SupplierName, v.customer_id AS CustomerId, c.supplier_code AS CustomerCode,
               c.supplier_name AS CustomerName, v.payment_method AS PaymentMethod, v.reference_no AS ReferenceNo,
               v.cheque_date AS ChequeDate, v.narration AS Narration, v.total_amount AS TotalAmount, v.status AS Status,
               v.posted_at AT TIME ZONE 'utc' AS PostedAt, v.posted_by AS PostedBy,
               v.cancelled_at AT TIME ZONE 'utc' AS CancelledAt, v.cancelled_by AS CancelledBy,
               v.created_at AT TIME ZONE 'utc' AS CreatedAt, v.created_by AS CreatedBy,
               v.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        """;

    // Suppliers and customers are both purchase.supplier rows.
    private const string VoucherFrom = """
        FROM accounting.voucher v
        LEFT JOIN purchase.supplier s ON s.supplier_id = v.supplier_id
        LEFT JOIN purchase.supplier c ON c.supplier_id = v.customer_id
        """;

    private const string VoucherFilter = """
        WHERE v.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR v.voucher_no ILIKE @Pattern OR v.reference_no ILIKE @Pattern
               OR v.narration ILIKE @Pattern OR s.supplier_name ILIKE @Pattern OR c.supplier_name ILIKE @Pattern)
          AND (CAST(@VoucherType AS text) IS NULL OR v.voucher_type = @VoucherType)
          AND (CAST(@Status AS text) IS NULL OR v.status = @Status)
          AND (CAST(@SupplierId AS int) IS NULL OR v.supplier_id = @SupplierId)
          AND (CAST(@CustomerId AS int) IS NULL OR v.customer_id = @CustomerId)
          AND (CAST(@From AS date) IS NULL OR v.voucher_date >= @From)
          AND (CAST(@To AS date) IS NULL OR v.voucher_date <= @To)
        """;

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public async Task<PagedResult<Voucher>> GetVouchersAsync(int organizationId, VoucherQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await RepositoryHelpers.PagedAsync<Voucher>(connection,
            $"SELECT count(*) {VoucherFrom} {VoucherFilter}",
            $"{VoucherSelect} {VoucherFrom} {VoucherFilter} ORDER BY v.voucher_date DESC, v.voucher_id DESC",
            new
            {
                Org = organizationId,
                Pattern = RepositoryHelpers.Pattern(query.Search),
                query.VoucherType,
                query.Status,
                query.SupplierId,
                query.CustomerId,
                query.From,
                query.To
            }, query.Limit, query.Offset, cancellationToken);
    }

    public async Task<Voucher?> GetVoucherAsync(int organizationId, long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        var voucher = await connection.QuerySingleOrDefaultAsync<Voucher>(new CommandDefinition(
            $"{VoucherSelect} {VoucherFrom} WHERE v.organization_id = @Org AND v.voucher_id = @Id",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
        if (voucher is null)
            return null;

        voucher.Lines = (await connection.QueryAsync<VoucherLine>(new CommandDefinition("""
            SELECT l.voucher_line_id AS Id, l.line_no AS LineNo, l.acc_sub_class_id AS AccountId,
                   coalesce(trim(a.sub_code), '') AS AccountCode, coalesce(a.sub_name, '') AS AccountName,
                   l.supplier_id AS PartyId, p.supplier_code AS PartyCode, p.supplier_name AS PartyName,
                   l.debit_amount AS DebitAmount, l.credit_amount AS CreditAmount,
                   l.lk_currency_id AS CurrencyLookupDataId, cur.lookup_data_name AS Currency,
                   l.local_exchange_rate AS LocalExchangeRate, l.line_narration AS LineNarration
            FROM accounting.voucher_line l
            JOIN accounting.acc_sub_class a ON a.acc_sub_class_id = l.acc_sub_class_id
            LEFT JOIN purchase.supplier p ON p.supplier_id = l.supplier_id
            LEFT JOIN public.lookup_data cur ON cur.lookup_data_id = l.lk_currency_id
            WHERE l.voucher_id = @Id
            ORDER BY l.line_no
            """, new { Id = id }, cancellationToken: cancellationToken))).AsList();
        return voucher;
    }

    public async Task<long> SaveVoucherAsync(int organizationId, Voucher voucher, bool post, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var parameters = new
        {
            Org = organizationId,
            voucher.Id,
            voucher.VoucherType,
            voucher.VoucherDate,
            voucher.SupplierId,
            voucher.CustomerId,
            voucher.PaymentMethod,
            voucher.ReferenceNo,
            voucher.ChequeDate,
            voucher.Narration,
            TotalAmount = voucher.Lines.Sum(l => l.DebitAmount),
            UserId = userId,
            VoucherNo = voucher.Id == 0
                ? await DocumentNumbers.NextAsync(connection, transaction, organizationId,
                    VoucherType.NumberSeries(voucher.VoucherType), cancellationToken)
                : null
        };

        long id;
        if (voucher.Id == 0)
        {
            id = await connection.QuerySingleAsync<long>(new CommandDefinition($"""
                INSERT INTO accounting.voucher (
                    organization_id, voucher_no, voucher_type, voucher_date, supplier_id, customer_id, payment_method,
                    reference_no, cheque_date, narration, total_amount, status, created_at, created_by)
                VALUES (
                    @Org, @VoucherNo, @VoucherType, @VoucherDate, @SupplierId, @CustomerId, @PaymentMethod,
                    @ReferenceNo, @ChequeDate, @Narration, @TotalAmount, 'Draft', {SecurityDb.NowUtc}, @UserId)
                RETURNING voucher_id
                """, parameters, transaction, cancellationToken: cancellationToken));
        }
        else
        {
            id = voucher.Id;
            await LockDraftAsync(connection, transaction, organizationId, id, cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE accounting.voucher
                SET voucher_type = @VoucherType, voucher_date = @VoucherDate, supplier_id = @SupplierId,
                    customer_id = @CustomerId, payment_method = @PaymentMethod, reference_no = @ReferenceNo,
                    cheque_date = @ChequeDate, narration = @Narration, total_amount = @TotalAmount,
                    updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                WHERE organization_id = @Org AND voucher_id = @Id
                """, parameters, transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM accounting.voucher_line WHERE voucher_id = @Id", new { Id = id }, transaction,
                cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO accounting.voucher_line (voucher_id, line_no, acc_sub_class_id, supplier_id, debit_amount,
                                                 credit_amount, lk_currency_id, local_exchange_rate, line_narration)
            SELECT @Id, x.line_no, x.acc_sub_class_id, x.supplier_id, x.debit_amount, x.credit_amount, x.lk_currency_id,
                   x.local_exchange_rate, x.line_narration
            FROM unnest(@LineNos, @AccountIds, @PartyIds, @Debits, @Credits, @Currencies, @Rates, @Narrations)
                 AS x(line_no, acc_sub_class_id, supplier_id, debit_amount, credit_amount, lk_currency_id,
                      local_exchange_rate, line_narration)
            """, new
            {
                Id = id,
                LineNos = voucher.Lines.Select(l => l.LineNo).ToArray(),
                AccountIds = voucher.Lines.Select(l => l.AccountId).ToArray(),
                PartyIds = voucher.Lines.Select(l => l.PartyId).ToArray(),
                Debits = voucher.Lines.Select(l => l.DebitAmount).ToArray(),
                Credits = voucher.Lines.Select(l => l.CreditAmount).ToArray(),
                Currencies = voucher.Lines.Select(l => l.CurrencyLookupDataId).ToArray(),
                Rates = voucher.Lines.Select(l => l.LocalExchangeRate).ToArray(),
                Narrations = voucher.Lines.Select(l => l.LineNarration).ToArray()
            }, transaction, cancellationToken: cancellationToken));

        if (post)
            await PostAsync(connection, transaction, organizationId, id, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    private static async Task<string> LockAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, long id, CancellationToken cancellationToken)
        => await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
               "SELECT status FROM accounting.voucher WHERE organization_id = @Org AND voucher_id = @Id FOR UPDATE",
               new { Org = organizationId, Id = id }, transaction, cancellationToken: cancellationToken))
           ?? throw new BusinessRuleException("The voucher doesn't exist.");

    private static async Task LockDraftAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, long id, CancellationToken cancellationToken)
    {
        var status = await LockAsync(connection, transaction, organizationId, id, cancellationToken);
        if (status != DocumentStatus.Draft)
            throw new BusinessRuleException(status == DocumentStatus.Posted
                ? "The voucher is posted and can't be changed. Cancel it and enter a new one."
                : "The voucher is cancelled and can't be changed.");
    }

    private static async Task PostAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, long id, int userId,
        CancellationToken cancellationToken)
    {
        var (lines, debit, credit) = await connection.QuerySingleAsync<(int, decimal, decimal)>(new CommandDefinition("""
            SELECT count(*)::int, coalesce(sum(debit_amount), 0), coalesce(sum(credit_amount), 0)
            FROM accounting.voucher_line WHERE voucher_id = @Id
            """, new { Id = id }, transaction, cancellationToken: cancellationToken));
        if (lines < 2 || debit == 0)
            throw new BusinessRuleException("A voucher needs at least one debit and one credit line.");
        if (debit != credit)
            throw new BusinessRuleException($"Debits ({debit:N2}) and credits ({credit:N2}) must be equal to post the voucher.");

        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE accounting.voucher
            SET status = 'Posted', total_amount = @Total, posted_at = {SecurityDb.NowUtc}, posted_by = @UserId
            WHERE organization_id = @Org AND voucher_id = @Id
            """, new { Org = organizationId, Id = id, Total = debit, UserId = userId }, transaction,
            cancellationToken: cancellationToken));
    }

    public async Task<DeleteOutcome> DeleteVoucherAsync(int organizationId, long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        var status = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
            WITH deleted AS (
                DELETE FROM accounting.voucher WHERE organization_id = @Org AND voucher_id = @Id AND status = 'Draft'
                RETURNING status)
            SELECT coalesce((SELECT status FROM deleted),
                            (SELECT status FROM accounting.voucher WHERE organization_id = @Org AND voucher_id = @Id))
            """, new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
        return status switch
        {
            null => DeleteOutcome.NotFound,
            DocumentStatus.Draft => DeleteOutcome.Deleted,
            _ => DeleteOutcome.InUse
        };
    }

    public async Task PostVoucherAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockDraftAsync(connection, transaction, organizationId, id, cancellationToken);
        await PostAsync(connection, transaction, organizationId, id, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CancelVoucherAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var status = await LockAsync(connection, transaction, organizationId, id, cancellationToken);
        if (status != DocumentStatus.Posted)
            throw new BusinessRuleException(status == DocumentStatus.Draft
                ? "Delete a draft voucher instead of cancelling it."
                : "This voucher is already cancelled.");

        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE accounting.voucher
            SET status = 'Cancelled', cancelled_at = {SecurityDb.NowUtc}, cancelled_by = @UserId
            WHERE organization_id = @Org AND voucher_id = @Id
            """, new { Org = organizationId, Id = id, UserId = userId }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }
}
