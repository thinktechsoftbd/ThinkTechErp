using Dapper;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.ServiceInvoices;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.ServiceInvoices;

public interface IServiceInvoiceRepository
{
    Task<PagedResult<ServiceInvoice>> GetInvoicesAsync(int organizationId, ServiceInvoiceQuery query, CancellationToken cancellationToken = default);
    Task<ServiceInvoice?> GetInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    /// <summary>Creates (Id 0) or replaces a draft invoice with fully calculated lines; returns its id.</summary>
    Task<long> SaveInvoiceAsync(int organizationId, ServiceInvoice invoice, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    /// <summary>Posts a draft (it is then locked); false if there's no such draft.</summary>
    Task<bool> PostInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Back to draft (a service invoice moves no stock). With <paramref name="billVoucherUnposting"/>, its bill voucher
    /// is being unposted; otherwise a posted one refuses it.
    /// </summary>
    Task UnpostInvoiceAsync(int organizationId, long id, int userId, bool billVoucherUnposting, CancellationToken cancellationToken = default);
}

public sealed class ServiceInvoiceRepository(IDbConnectionFactory connectionFactory) : IServiceInvoiceRepository
{
    private const string InvoiceSelect = """
        SELECT i.service_invoice_id AS Id, i.object_id AS ObjectId, i.invoice_no AS InvoiceNo, i.invoice_date AS InvoiceDate,
               i.sale_type AS SaleType, i.due_date AS DueDate, i.customer_id AS CustomerId, c.supplier_code AS CustomerCode,
               coalesce(c.supplier_name, i.customer_name) AS CustomerName, coalesce(i.customer_mobile, c.mobile) AS CustomerMobile,
               nullif(concat_ws(', ', c.address_line1, c.city), '') AS CustomerAddress,
               i.warehouse_id AS WarehouseId, w.warehouse_name AS WarehouseName,
               i.currency_lookup_data_id AS CurrencyLookupDataId, cur.lookup_data_name AS Currency, i.exchange_rate AS ExchangeRate,
               i.subtotal_amount AS SubtotalAmount, i.discount_amount AS DiscountAmount,
               i.extra_discount_amount AS ExtraDiscountAmount, i.tax_amount AS TaxAmount, i.charge_amount AS ChargeAmount,
               i.service_amount AS ServiceAmount, i.round_off_amount AS RoundOffAmount, i.total_amount AS TotalAmount,
               i.paid_amount AS PaidAmount, i.payment_method AS PaymentMethod, i.payment_provider AS PaymentProvider,
               i.payment_reference_no AS PaymentReferenceNo, i.settled_amount AS SettledAmount,
               i.inv_payment_source_d_id AS InvPaymentSourceDId,
               psh.id AS PaymentSourceId, psh.source_name_en AS PaymentSourceName, psd.payment_for AS PaymentSourceType,
               i.total_amount - i.settled_amount AS DueAmount, i.status AS Status, i.payment_status AS PaymentStatus,
               bv.voucher_id AS BillVoucherId, bv.voucher_ref_no AS BillVoucherRefNo, bv.status AS BillVoucherStatus,
               (SELECT count(*) FROM public.service_invoice_line l WHERE l.service_invoice_id = i.service_invoice_id)::int AS LineCount,
               i.posted_at AT TIME ZONE 'utc' AS PostedAt, i.posted_by AS PostedBy, i.service_note AS ServiceNote,
               i.remarks AS Remarks, i.created_at AT TIME ZONE 'utc' AS CreatedAt, i.created_by AS CreatedBy,
               i.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM public.service_invoice i
        LEFT JOIN public.voucher bv ON bv.service_invoice_id = i.service_invoice_id AND NOT bv.is_deleted AND bv.status <> 'Cancelled'
        LEFT JOIN public.supplier c ON c.supplier_id = i.customer_id
        JOIN public.warehouse w ON w.warehouse_id = i.warehouse_id
        LEFT JOIN public.inv_payment_source_d psd ON psd.id = i.inv_payment_source_d_id
        LEFT JOIN public.inv_payment_source_h psh ON psh.id = psd.inv_payment_source_h_id
        JOIN public.lookup_data cur ON cur.lookup_data_id = i.currency_lookup_data_id
        """;

    private const string InvoiceFilter = """
        WHERE i.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR i.invoice_no ILIKE @Pattern OR c.supplier_name ILIKE @Pattern
               OR c.supplier_code ILIKE @Pattern OR c.mobile ILIKE @Pattern OR i.customer_name ILIKE @Pattern
               OR i.customer_mobile ILIKE @Pattern OR i.service_note ILIKE @Pattern)
          AND (CAST(@Status AS text) IS NULL OR i.status = @Status)
          AND (CAST(@CustomerId AS int) IS NULL OR i.customer_id = @CustomerId)
          AND (CAST(@From AS date) IS NULL OR i.invoice_date >= @From)
          AND (CAST(@To AS date) IS NULL OR i.invoice_date <= @To)
          AND (CAST(@SaleType AS text) IS NULL OR i.sale_type = @SaleType)
          AND (CAST(@PaymentStatus AS text) IS NULL OR i.payment_status = @PaymentStatus)
        """;

    // The columns the list may be sorted by (never interpolate anything else into ORDER BY).
    private static readonly Dictionary<string, string> InvoiceSorts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["number"] = "i.invoice_no",
        ["date"] = "i.invoice_date",
        ["customer"] = "lower(coalesce(c.supplier_name, i.customer_name))",
        ["type"] = "i.sale_type",
        ["service"] = "i.service_amount",
        ["total"] = "i.total_amount",
        ["due"] = "(i.total_amount - i.settled_amount)",
        ["payment"] = "i.payment_status",
        ["status"] = "i.status"
    };

    public async Task<PagedResult<ServiceInvoice>> GetInvoicesAsync(int organizationId, ServiceInvoiceQuery query, CancellationToken cancellationToken = default)
    {
        // Newest first by default, and after the chosen column.
        var order = query.Sort is not null && InvoiceSorts.TryGetValue(query.Sort, out var column)
            ? $"{column} {(query.Descending ? "DESC" : "ASC")}, i.invoice_date DESC, i.service_invoice_id DESC"
            : "i.invoice_date DESC, i.service_invoice_id DESC";
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<ServiceInvoice>(connection,
            $"SELECT count(*) FROM public.service_invoice i LEFT JOIN public.supplier c ON c.supplier_id = i.customer_id {InvoiceFilter}",
            $"{InvoiceSelect} {InvoiceFilter} ORDER BY {order}",
            new
            {
                Org = organizationId,
                Pattern = RepositoryHelpers.Pattern(query.Search),
                query.Status,
                query.CustomerId,
                query.From,
                query.To,
                query.SaleType,
                query.PaymentStatus
            }, query.Limit, query.Offset, cancellationToken);
    }

    public async Task<ServiceInvoice?> GetInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var invoice = await connection.QuerySingleOrDefaultAsync<ServiceInvoice>(new CommandDefinition(
            $"{InvoiceSelect} WHERE i.organization_id = @Org AND i.service_invoice_id = @Id",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
        if (invoice is null)
            return null;

        invoice.Lines = (await connection.QueryAsync<ServiceInvoiceLine>(new CommandDefinition("""
            SELECT l.service_invoice_line_id AS Id, l.line_no AS LineNo, l.product_id AS ProductId,
                   p.product_code AS ProductCode, p.product_name AS ProductName, p.product_type AS ProductType,
                   p.tracking_type AS TrackingType, l.description AS Description,
                   l.unit_id AS UnitId, u.unit_code AS UnitCode, bu.unit_code AS BaseUnitCode,
                   l.conversion_factor AS ConversionFactor, l.quantity AS Quantity, l.base_quantity AS BaseQuantity,
                   l.batch_id AS BatchId, b.batch_no AS BatchNo, b.expiry_date AS ExpiryDate, l.unit_price AS UnitPrice,
                   l.discount_percent AS DiscountPercent, l.discount_amount AS DiscountAmount, l.tax_group_id AS TaxGroupId,
                   tg.tax_group_name AS TaxGroupName, l.tax_amount AS TaxAmount, l.line_total AS LineTotal, l.remarks AS Remarks
            FROM public.service_invoice_line l
            JOIN public.product p ON p.product_id = l.product_id
            JOIN public.unit u ON u.unit_id = l.unit_id
            JOIN public.unit bu ON bu.unit_id = p.base_unit_id
            LEFT JOIN public.tax_group tg ON tg.tax_group_id = l.tax_group_id
            LEFT JOIN public.batch b ON b.batch_id = l.batch_id
            WHERE l.service_invoice_id = @Id
            ORDER BY l.line_no
            """, new { Id = id }, cancellationToken: cancellationToken))).AsList();
        return invoice;
    }

    public async Task<long> SaveInvoiceAsync(int organizationId, ServiceInvoice invoice, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var parameters = new DynamicParameters(new
        {
            Org = organizationId,
            invoice.InvoiceDate,
            invoice.SaleType,
            invoice.DueDate,
            invoice.CustomerId,
            CustomerName = invoice.CustomerId is null ? invoice.CustomerName : null,
            invoice.CustomerMobile,
            invoice.WarehouseId,
            invoice.CurrencyLookupDataId,
            invoice.ExchangeRate,
            invoice.SubtotalAmount,
            invoice.DiscountAmount,
            invoice.ExtraDiscountAmount,
            invoice.TaxAmount,
            invoice.ChargeAmount,
            invoice.ServiceAmount,
            invoice.RoundOffAmount,
            invoice.TotalAmount,
            invoice.PaidAmount,
            invoice.PaymentMethod,
            invoice.PaymentProvider,
            invoice.PaymentReferenceNo,
            invoice.InvPaymentSourceDId,
            SettledAmount = invoice.PaidAmount,
            invoice.PaymentStatus,
            invoice.ServiceNote,
            invoice.Remarks,
            UserId = userId
        });

        long id;
        if (invoice.Id == 0)
        {
            // FC/Srv/2026-11-0001: restarts each month (by the invoice date); SVI-00001 when the organization has no code.
            parameters.Add("Number",
                await DocumentNumbers.NextMonthlyAsync(connection, transaction, organizationId, DocumentType.ServiceInvoice, "Srv",
                    invoice.InvoiceDate, cancellationToken)
                ?? await DocumentNumbers.NextAsync(connection, transaction, organizationId, DocumentType.ServiceInvoice, cancellationToken));
            id = await connection.QuerySingleAsync<long>(new CommandDefinition($"""
                INSERT INTO public.service_invoice (
                    organization_id, invoice_no, invoice_date, sale_type, due_date, customer_id, customer_name,
                    customer_mobile, warehouse_id, currency_lookup_data_id, exchange_rate, subtotal_amount,
                    discount_amount, extra_discount_amount, tax_amount, charge_amount, service_amount, round_off_amount,
                    total_amount, paid_amount, payment_method, payment_provider, payment_reference_no,
                    inv_payment_source_d_id, settled_amount, status, payment_status, service_note, remarks, created_at, created_by)
                VALUES (
                    @Org, @Number, @InvoiceDate, @SaleType, @DueDate, @CustomerId, @CustomerName,
                    @CustomerMobile, @WarehouseId, @CurrencyLookupDataId, @ExchangeRate, @SubtotalAmount,
                    @DiscountAmount, @ExtraDiscountAmount, @TaxAmount, @ChargeAmount, @ServiceAmount, @RoundOffAmount,
                    @TotalAmount, @PaidAmount, @PaymentMethod, @PaymentProvider, @PaymentReferenceNo,
                    @InvPaymentSourceDId, @SettledAmount, 'Draft', @PaymentStatus, @ServiceNote, @Remarks, {SecurityDb.NowUtc}, @UserId)
                RETURNING service_invoice_id
                """, parameters, transaction, cancellationToken: cancellationToken));
        }
        else
        {
            id = invoice.Id;
            var status = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
                SELECT status FROM public.service_invoice WHERE organization_id = @Org AND service_invoice_id = @Id
                FOR UPDATE
                """, new { Org = organizationId, Id = id }, transaction, cancellationToken: cancellationToken))
                ?? throw new BusinessRuleException("The service invoice doesn't exist.");
            if (status != DocumentStatus.Draft)
                throw new BusinessRuleException("Only a draft service invoice can be changed.");
            parameters.Add("Id", id);
            await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE public.service_invoice
                SET invoice_date = @InvoiceDate, sale_type = @SaleType, due_date = @DueDate, customer_id = @CustomerId,
                    customer_name = @CustomerName, customer_mobile = @CustomerMobile, warehouse_id = @WarehouseId,
                    currency_lookup_data_id = @CurrencyLookupDataId, exchange_rate = @ExchangeRate,
                    subtotal_amount = @SubtotalAmount, discount_amount = @DiscountAmount,
                    extra_discount_amount = @ExtraDiscountAmount, tax_amount = @TaxAmount, charge_amount = @ChargeAmount,
                    service_amount = @ServiceAmount, round_off_amount = @RoundOffAmount, total_amount = @TotalAmount,
                    paid_amount = @PaidAmount, payment_method = @PaymentMethod, payment_provider = @PaymentProvider,
                    payment_reference_no = @PaymentReferenceNo, inv_payment_source_d_id = @InvPaymentSourceDId,
                    settled_amount = @SettledAmount, payment_status = @PaymentStatus, service_note = @ServiceNote, remarks = @Remarks,
                    updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                WHERE service_invoice_id = @Id;
                DELETE FROM public.service_invoice_line WHERE service_invoice_id = @Id;
                """, parameters, transaction, cancellationToken: cancellationToken));
        }

        if (invoice.Lines.Count > 0)
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO public.service_invoice_line (
                    service_invoice_id, line_no, product_id, description, unit_id, conversion_factor, quantity, batch_id,
                    unit_price, discount_percent, discount_amount, tax_group_id, tax_amount, line_total, remarks)
                VALUES (
                    @DocumentId, @LineNo, @ProductId, @Description, @UnitId, @ConversionFactor, @Quantity, @BatchId,
                    @UnitPrice, @DiscountPercent, @DiscountAmount, @TaxGroupId, @TaxAmount, @LineTotal, @Remarks)
                """, invoice.Lines.Select(line => new
                {
                    DocumentId = id,
                    line.LineNo,
                    line.ProductId,
                    line.Description,
                    line.UnitId,
                    line.ConversionFactor,
                    line.Quantity,
                    line.BatchId,
                    line.UnitPrice,
                    line.DiscountPercent,
                    line.DiscountAmount,
                    line.TaxGroupId,
                    line.TaxAmount,
                    line.LineTotal,
                    line.Remarks
                }), transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<DeleteOutcome> DeleteInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var status = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT status FROM public.service_invoice WHERE organization_id = @Org AND service_invoice_id = @Id",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
        if (status is null)
            return DeleteOutcome.NotFound;
        if (status != DocumentStatus.Draft)
            return DeleteOutcome.InUse;
        // The lines go with it (ON DELETE CASCADE); so does an unposted bill voucher from an earlier finalize.
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await InvoiceVouchers.DetachForDeleteAsync(connection, transaction, "service_invoice_id", id, cancellationToken);
        var outcome = await RepositoryHelpers.DeleteAsync(connection, new CommandDefinition(
            "DELETE FROM public.service_invoice WHERE organization_id = @Org AND service_invoice_id = @Id AND status = 'Draft'",
            new { Org = organizationId, Id = id }, transaction, cancellationToken: cancellationToken));
        if (outcome == DeleteOutcome.Deleted)
            await transaction.CommitAsync(cancellationToken);
        return outcome;
    }

    public async Task UnpostInvoiceAsync(int organizationId, long id, int userId, bool billVoucherUnposting, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var status = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
            SELECT status FROM public.service_invoice WHERE organization_id = @Org AND service_invoice_id = @Id
            FOR UPDATE
            """, new { Org = organizationId, Id = id }, transaction, cancellationToken: cancellationToken))
            ?? throw new BusinessRuleException("The service invoice doesn't exist.");
        if (status != DocumentStatus.Posted)
            throw new BusinessRuleException(status == DocumentStatus.Draft
                ? "This service invoice is already a draft."
                : "A cancelled service invoice can't go back to draft.");
        if (!billVoucherUnposting)
            await InvoiceVouchers.RequireNoPostedBillVoucherAsync(connection, transaction, "service_invoice_id", id, cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.service_invoice
            SET status = 'Draft', posted_at = NULL, posted_by = NULL, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE service_invoice_id = @Id
            """, new { Id = id, UserId = userId }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> PostInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.service_invoice
            SET status = 'Posted', posted_at = {SecurityDb.NowUtc}, posted_by = @UserId,
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE organization_id = @Org AND service_invoice_id = @Id AND status = 'Draft'
            """, new { Org = organizationId, Id = id, UserId = userId }, cancellationToken: cancellationToken)) > 0;
    }
}
