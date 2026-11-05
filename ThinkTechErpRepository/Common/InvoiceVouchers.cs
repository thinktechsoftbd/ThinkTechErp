using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Common;

namespace ThinkTechErpRepository.Common;

/// <summary>The Bill Voucher an invoice was finalized into (voucher.purchase_invoice_id / sales_invoice_id).</summary>
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
}
