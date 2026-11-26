using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Warranty;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Warranty;

/// <summary>
/// Warranty cards follow the sales documents, inside their transactions: issued when an invoice posts, voided when
/// it is cancelled, and voided (or restored) as units go back on sales returns.
/// </summary>
internal static class WarrantyCards
{
    /// <summary>The void reason of a card whose unit came back on a sales return (cancelling the return restores it).</summary>
    public const string ReturnedReason = "Returned to the shop on a sales return.";

    /// <summary>The supplier a serial came in from: its latest posted purchase invoice, else goods receipt.</summary>
    public const string SerialSupplierSql = """
        coalesce(
            (SELECT pi.supplier_id FROM public.purchase_invoice_serial pis
             JOIN public.purchase_invoice_line pil ON pil.purchase_invoice_line_id = pis.purchase_invoice_line_id
             JOIN public.purchase_invoice pi ON pi.purchase_invoice_id = pil.purchase_invoice_id
             WHERE pis.serial_number_id = s.serial_number_id AND pi.status = 'Posted' AND pi.supplier_id IS NOT NULL
             ORDER BY pi.invoice_date DESC, pi.purchase_invoice_id DESC LIMIT 1),
            (SELECT gr.supplier_id FROM public.goods_receipt_serial grs
             JOIN public.goods_receipt_line grl ON grl.goods_receipt_line_id = grs.goods_receipt_line_id
             JOIN public.goods_receipt gr ON gr.goods_receipt_id = grl.goods_receipt_id
             WHERE grs.serial_number_id = s.serial_number_id AND gr.status = 'Posted' AND gr.supplier_id IS NOT NULL
             ORDER BY gr.receipt_date DESC, gr.goods_receipt_id DESC LIMIT 1))
        """;

    private sealed class SoldUnit
    {
        public long LineId { get; set; }
        public int ProductId { get; set; }
        public decimal LineTotal { get; set; }
        public long? SerialNumberId { get; set; }
        public string? SerialNo { get; set; }
        public decimal Quantity { get; set; }
        public DateOnly? SupplierWarrantyEndDate { get; set; }
        public int? SupplierId { get; set; }
        public int? PolicyId { get; set; }
        public int WarrantyMonths { get; set; }
        public int ReplacementDays { get; set; }
        public bool CoversParts { get; set; }
        public bool CoversLabor { get; set; }
        public string? Terms { get; set; }
    }

    private sealed class InvoiceParty
    {
        public DateOnly InvoiceDate { get; set; }
        public int? CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerMobile { get; set; }
    }

    private sealed class BilledJob
    {
        public long Id { get; set; }
        public string JobType { get; set; } = string.Empty;
        public long? CardId { get; set; }
        public int? CardProductId { get; set; }
    }

    /// <summary>
    /// Issues the cards of a posted invoice: one per serial (or per line without serials) for products sold with an
    /// active warranty policy. On the invoice of a warranty job, free (0) lines are the warranty's work: the line of
    /// the claimed product is the replacement unit, whose card carries the old card's remaining warranty (the old card
    /// becomes Replaced); other free parts get no card of their own.
    /// </summary>
    public static async Task IssueForInvoiceAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, long invoiceId, int userId,
        CancellationToken cancellationToken)
    {
        var job = await connection.QuerySingleOrDefaultAsync<BilledJob>(new CommandDefinition("""
            SELECT j.service_job_id AS Id, j.job_type AS JobType, w.warranty_registration_id AS CardId, w.product_id AS CardProductId
            FROM public.service_job j
            LEFT JOIN public.warranty_registration w ON w.warranty_registration_id = j.warranty_registration_id AND w.status = 'Active'
            WHERE j.sales_invoice_id = @Id
            """, new { Id = invoiceId }, transaction, cancellationToken: cancellationToken));

        if (job is not null)
        {
            // Each item points at the invoice line it became (the invoice was made from the items, line for line).
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE public.service_job_item i SET sales_invoice_line_id = l.sales_invoice_line_id
                FROM public.sales_invoice_line l
                WHERE i.service_job_id = @JobId AND l.sales_invoice_id = @Id AND l.line_no = i.line_no AND l.product_id = i.product_id
                """, new { JobId = job.Id, Id = invoiceId }, transaction, cancellationToken: cancellationToken));
        }

        var party = await connection.QuerySingleAsync<InvoiceParty>(new CommandDefinition("""
            SELECT i.invoice_date AS InvoiceDate, i.customer_id AS CustomerId,
                   CASE WHEN i.customer_id IS NULL THEN i.customer_name END AS CustomerName,
                   coalesce(i.customer_mobile, c.mobile) AS CustomerMobile
            FROM public.sales_invoice i LEFT JOIN public.supplier c ON c.supplier_id = i.customer_id
            WHERE i.sales_invoice_id = @Id
            """, new { Id = invoiceId }, transaction, cancellationToken: cancellationToken));

        var units = (await connection.QueryAsync<SoldUnit>(new CommandDefinition($"""
            SELECT l.sales_invoice_line_id AS LineId, l.product_id AS ProductId, l.line_total AS LineTotal,
                   s.serial_number_id AS SerialNumberId, s.serial_no AS SerialNo,
                   CASE WHEN s.serial_number_id IS NULL
                        THEN coalesce(l.base_quantity, l.quantity * l.conversion_factor) ELSE 1 END AS Quantity,
                   s.supplier_warranty_end_date AS SupplierWarrantyEndDate,
                   CASE WHEN s.serial_number_id IS NULL THEN NULL ELSE {SerialSupplierSql} END AS SupplierId,
                   wp.warranty_policy_id AS PolicyId, coalesce(wp.warranty_months, 0) AS WarrantyMonths,
                   coalesce(wp.replacement_days, 0) AS ReplacementDays, coalesce(wp.covers_parts, true) AS CoversParts,
                   coalesce(wp.covers_labor, true) AS CoversLabor, wp.terms AS Terms
            FROM public.sales_invoice_line l
            JOIN public.product p ON p.product_id = l.product_id
            LEFT JOIN public.warranty_policy wp ON wp.warranty_policy_id = p.warranty_policy_id AND wp.is_active
            LEFT JOIN public.sales_invoice_serial ss ON ss.sales_invoice_line_id = l.sales_invoice_line_id
            LEFT JOIN public.serial_number s ON s.serial_number_id = ss.serial_number_id
            WHERE l.sales_invoice_id = @Id
            ORDER BY l.line_no, s.serial_no
            """, new { Id = invoiceId }, transaction, cancellationToken: cancellationToken))).AsList();

        var replaced = false;
        foreach (var unit in units)
        {
            if (job is not null && unit.LineTotal == 0)
            {
                if (!replaced && job.JobType == ServiceJobType.Warranty && job.CardId is long cardId && unit.ProductId == job.CardProductId)
                {
                    replaced = true;
                    await IssueReplacementAsync(connection, transaction, organizationId, invoiceId, unit, cardId, userId, cancellationToken);
                }
                continue;
            }
            if (unit.PolicyId is null)
                continue;

            var (end, replacementEnd) = WarrantyRules.Period(party.InvoiceDate, unit.WarrantyMonths, unit.ReplacementDays);
            var number = await DocumentNumbers.NextAsync(connection, transaction, organizationId, DocumentType.WarrantyCard, cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition($"""
                INSERT INTO public.warranty_registration (
                    organization_id, warranty_no, source, sales_invoice_id, sales_invoice_line_id, product_id,
                    serial_number_id, serial_no, quantity, customer_id, customer_name, customer_mobile, warranty_policy_id,
                    start_date, replacement_end_date, end_date, covers_parts, covers_labor, terms, supplier_id,
                    supplier_warranty_end_date, status, created_at, created_by)
                VALUES (
                    @Org, @Number, 'Sale', @InvoiceId, @LineId, @ProductId,
                    @SerialNumberId, @SerialNo, @Quantity, @CustomerId, @CustomerName, @CustomerMobile, @PolicyId,
                    @Start, @ReplacementEnd, @End, @CoversParts, @CoversLabor, @Terms, @SupplierId,
                    @SupplierWarrantyEndDate, 'Active', {SecurityDb.NowUtc}, @UserId)
                """, new
                {
                    Org = organizationId,
                    Number = number,
                    InvoiceId = invoiceId,
                    unit.LineId,
                    unit.ProductId,
                    unit.SerialNumberId,
                    unit.SerialNo,
                    unit.Quantity,
                    party.CustomerId,
                    party.CustomerName,
                    party.CustomerMobile,
                    unit.PolicyId,
                    Start = party.InvoiceDate,
                    ReplacementEnd = replacementEnd,
                    End = end,
                    unit.CoversParts,
                    unit.CoversLabor,
                    unit.Terms,
                    unit.SupplierId,
                    unit.SupplierWarrantyEndDate,
                    UserId = userId
                }, transaction, cancellationToken: cancellationToken));
        }
    }

    private static async Task IssueReplacementAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, long invoiceId, SoldUnit unit,
        long cardId, int userId, CancellationToken cancellationToken)
    {
        var number = await DocumentNumbers.NextAsync(connection, transaction, organizationId, DocumentType.WarrantyCard, cancellationToken);
        // The old card first, so the unit's serial is free if the same serial comes back as the replacement.
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.warranty_registration SET status = 'Replaced', updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE warranty_registration_id = @CardId AND status = 'Active';

            INSERT INTO public.warranty_registration (
                organization_id, warranty_no, source, sales_invoice_id, sales_invoice_line_id, product_id,
                serial_number_id, serial_no, quantity, customer_id, customer_name, customer_mobile, warranty_policy_id,
                start_date, replacement_end_date, end_date, covers_parts, covers_labor, terms, supplier_id,
                supplier_warranty_end_date, replaces_registration_id, status, remarks, created_at, created_by)
            SELECT o.organization_id, @Number, 'Replacement', @InvoiceId, @LineId, @ProductId,
                   @SerialNumberId, @SerialNo, @Quantity, o.customer_id, o.customer_name, o.customer_mobile, o.warranty_policy_id,
                   o.start_date, o.replacement_end_date, o.end_date, o.covers_parts, o.covers_labor, o.terms, @SupplierId,
                   @SupplierWarrantyEndDate, o.warranty_registration_id, 'Active',
                   'Replaces ' || o.warranty_no || coalesce(' (serial ' || o.serial_no || ')', '') || '.',
                   {SecurityDb.NowUtc}, @UserId
            FROM public.warranty_registration o
            WHERE o.warranty_registration_id = @CardId;
            """, new
            {
                CardId = cardId,
                Number = number,
                InvoiceId = invoiceId,
                unit.LineId,
                unit.ProductId,
                unit.SerialNumberId,
                unit.SerialNo,
                unit.Quantity,
                unit.SupplierId,
                unit.SupplierWarrantyEndDate,
                UserId = userId
            }, transaction, cancellationToken: cancellationToken));
    }

    /// <summary>
    /// A cancelled invoice voids its cards. Replacement units on it give the replaced cards back their cover.
    /// </summary>
    public static Task VoidForInvoiceAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long invoiceId, string invoiceNo, int userId,
        CancellationToken cancellationToken, string? reason = null)
        => connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.warranty_registration r
            SET status = 'Void', void_reason = @Reason, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE r.sales_invoice_id = @Id AND r.status = 'Active';

            UPDATE public.warranty_registration o
            SET status = 'Active', updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            FROM public.warranty_registration r
            WHERE r.sales_invoice_id = @Id AND r.source = 'Replacement' AND r.void_reason = @Reason
              AND o.warranty_registration_id = r.replaces_registration_id AND o.status = 'Replaced';
            """, new { Id = invoiceId, Reason = reason ?? $"Sales invoice {invoiceNo} was cancelled.", UserId = userId },
            transaction, cancellationToken: cancellationToken));

    /// <summary>
    /// Brings an invoice's cards in line with its posted returns, after a return is posted or cancelled: a serial unit
    /// taken back loses its card (and gets it back when the return is cancelled); a card without serials covers what
    /// is still with the customer.
    /// </summary>
    public static Task SyncReturnsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long invoiceId, int userId, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition($"""
            WITH units AS (
                SELECT w.warranty_registration_id AS id,
                       EXISTS (SELECT 1 FROM public.sales_return_serial rs
                               JOIN public.sales_return_line rl ON rl.sales_return_line_id = rs.sales_return_line_id
                               JOIN public.sales_return r ON r.sales_return_id = rl.sales_return_id
                               WHERE r.status = 'Posted' AND rl.sales_invoice_line_id = w.sales_invoice_line_id
                                 AND rs.serial_number_id = w.serial_number_id) AS returned
                FROM public.warranty_registration w
                WHERE w.sales_invoice_id = @Id AND w.serial_number_id IS NOT NULL)
            UPDATE public.warranty_registration w
            SET status = CASE WHEN u.returned THEN 'Void' ELSE 'Active' END,
                void_reason = CASE WHEN u.returned THEN @Reason END,
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            FROM units u
            WHERE w.warranty_registration_id = u.id
              AND ((u.returned AND w.status = 'Active') OR (NOT u.returned AND w.status = 'Void' AND w.void_reason = @Reason));

            WITH lines AS (
                SELECT w.warranty_registration_id AS id,
                       coalesce(l.base_quantity, l.quantity * l.conversion_factor) - l.returned_quantity * l.conversion_factor AS remaining
                FROM public.warranty_registration w
                JOIN public.sales_invoice_line l ON l.sales_invoice_line_id = w.sales_invoice_line_id
                WHERE w.sales_invoice_id = @Id AND w.serial_number_id IS NULL
                  AND (w.status = 'Active' OR (w.status = 'Void' AND w.void_reason = @Reason)))
            UPDATE public.warranty_registration w
            SET quantity = CASE WHEN x.remaining > 0 THEN round(x.remaining, 4) ELSE w.quantity END,
                status = CASE WHEN x.remaining > 0 THEN 'Active' ELSE 'Void' END,
                void_reason = CASE WHEN x.remaining > 0 THEN NULL ELSE @Reason END,
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            FROM lines x
            WHERE w.warranty_registration_id = x.id
              AND (CASE WHEN x.remaining > 0 THEN w.status <> 'Active' OR w.quantity <> round(x.remaining, 4)
                        ELSE w.status = 'Active' END);
            """, new { Id = invoiceId, Reason = ReturnedReason, UserId = userId }, transaction, cancellationToken: cancellationToken));
}
