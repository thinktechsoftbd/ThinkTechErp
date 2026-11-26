using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Common;

/// <summary>The Bill Voucher an invoice was finalized into (voucher.purchase_invoice_id / sales_invoice_id / service_invoice_id).</summary>
internal static class InvoiceVouchers
{
    /// <summary>
    /// A finalized invoice stays locked while its Bill Voucher is there (not deleted or cancelled): the voucher
    /// has to be unposted and deleted first.
    /// </summary>
    /// <param name="column">purchase_invoice_id or sales_invoice_id.</param>
    public static async Task RequireNoBillVoucherAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string column, long invoiceId, CancellationToken cancellationToken)
    {
        var voucherRefNo = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition($"""
            SELECT voucher_ref_no FROM public.voucher
            WHERE {column} = @Id AND NOT is_deleted AND status <> 'Cancelled'
            """, new { Id = invoiceId }, transaction, cancellationToken: cancellationToken));
        if (voucherRefNo is not null)
            throw new BusinessRuleException(
                $"The invoice is finalized: unpost and delete its bill voucher {voucherRefNo} (Accounting → Vouchers) first.");
    }

    /// <summary>
    /// Only a posted Bill Voucher locks the invoice: unposting it puts the invoice back to draft, and an unposted one is
    /// replaced when the invoice is finalized again.
    /// </summary>
    public static async Task RequireNoPostedBillVoucherAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string column, long invoiceId, CancellationToken cancellationToken)
    {
        var voucherRefNo = await PostedVoucherAsync(connection, transaction, column, invoiceId, cancellationToken);
        if (voucherRefNo is not null)
            throw new BusinessRuleException(
                $"The invoice is finalized: unpost its bill voucher {voucherRefNo} (Accounting → Vouchers) and the invoice goes back to draft.");
    }

    /// <summary>
    /// Before a draft invoice is deleted: its unposted Bill Voucher is deleted with it, and deleted or cancelled vouchers
    /// let go of it (the link would otherwise keep the invoice). Refused while a posted voucher is there.
    /// </summary>
    public static async Task DetachForDeleteAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string column, long invoiceId, CancellationToken cancellationToken)
    {
        await RequireNoPostedBillVoucherAsync(connection, transaction, column, invoiceId, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.voucher SET is_deleted = TRUE, updated_at = {SecurityDb.NowUtc}
            WHERE {column} = @Id AND NOT is_deleted AND status = 'Draft';
            UPDATE public.voucher SET {column} = NULL WHERE {column} = @Id;
            """, new { Id = invoiceId }, transaction, cancellationToken: cancellationToken));
    }

    private static Task<string?> PostedVoucherAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string column, long invoiceId, CancellationToken cancellationToken)
        => connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition($"""
            SELECT voucher_ref_no FROM public.voucher
            WHERE {column} = @Id AND NOT is_deleted AND status = 'Posted'
            """, new { Id = invoiceId }, transaction, cancellationToken: cancellationToken));
}
