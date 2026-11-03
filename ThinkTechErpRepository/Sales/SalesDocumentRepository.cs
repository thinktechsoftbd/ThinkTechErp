using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Purchase;
using ThinkTechErpDomain.Models.Sales;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Sales;

public interface ISalesDocumentRepository
{
    Task<PagedResult<SalesInvoice>> GetInvoicesAsync(int organizationId, SalesDocumentQuery query, CancellationToken cancellationToken = default);
    Task<SalesInvoice?> GetInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    /// <summary>
    /// Creates (Id 0) or replaces a draft invoice with fully calculated lines; serialIdsByLine holds the
    /// serial_number ids of each serial-tracked line, by LineNo. Returns its id.
    /// </summary>
    Task<long> SaveInvoiceAsync(int organizationId, SalesInvoice invoice, IReadOnlyDictionary<int, IReadOnlyList<long>> serialIdsByLine, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task PostInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task CancelInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OpenSalesInvoice>> GetOpenInvoicesAsync(int organizationId, int customerId, CancellationToken cancellationToken = default);
    /// <summary>serial_number ids of a product's items in stock in a warehouse, by serial number.</summary>
    Task<IReadOnlyDictionary<string, long>> FindSerialsInStockAsync(int organizationId, int productId, int warehouseId, IReadOnlyCollection<string> serialNos, CancellationToken cancellationToken = default);

    Task<PagedResult<SalesReturn>> GetReturnsAsync(int organizationId, SalesDocumentQuery query, CancellationToken cancellationToken = default);
    Task<SalesReturn?> GetReturnAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    /// <summary>Creates (Id 0) or replaces a draft return with fully calculated lines; returns its id.</summary>
    Task<long> SaveReturnAsync(int organizationId, SalesReturn salesReturn, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteReturnAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task PostReturnAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task CancelReturnAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    /// <summary>
    /// What may still be refunded in cash on an invoice: paid at the counter plus collected by receipts,
    /// less what posted refund returns (other than excludeReturnId) gave back.
    /// </summary>
    Task<decimal> GetRefundRoomAsync(int organizationId, long invoiceId, long? excludeReturnId, CancellationToken cancellationToken = default);

    Task<PagedResult<CustomerReceipt>> GetReceiptsAsync(int organizationId, SalesDocumentQuery query, CancellationToken cancellationToken = default);
    Task<CustomerReceipt?> GetReceiptAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    /// <summary>Records and posts a receipt with its allocations in one step; returns its id.</summary>
    Task<long> CreateReceiptAsync(int organizationId, CustomerReceipt receipt, int userId, CancellationToken cancellationToken = default);
    Task CancelReceiptAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task SetChequeStatusAsync(int organizationId, long id, string chequeStatus, int userId, CancellationToken cancellationToken = default);
}

public sealed partial class SalesDocumentRepository(IDbConnectionFactory connectionFactory) : ISalesDocumentRepository
{
    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static object ListParams(int organizationId, SalesDocumentQuery query) => new
    {
        Org = organizationId,
        Pattern = RepositoryHelpers.Pattern(query.Search),
        query.Status,
        query.CustomerId,
        query.From,
        query.To,
        query.SaleType,
        query.PaymentStatus
    };

    private static Task SetStatusAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string table, string key, long id, string status,
        int userId, CancellationToken cancellationToken, bool posted = false)
        => connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE {table} SET status = @Status, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                {(posted ? $", posted_at = {SecurityDb.NowUtc}, posted_by = @UserId" : "")}
            WHERE {key} = @Id
            """, new { Id = id, Status = status, UserId = userId }, transaction, cancellationToken: cancellationToken));

    /// <summary>Sets settled_amount (paid at the counter + allocations) and payment_status of these invoices.</summary>
    private static Task RecomputeSettlementAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IEnumerable<long> invoiceIds, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition("""
            UPDATE public.sales_invoice i
            SET settled_amount = least(i.total_amount, i.paid_amount + coalesce(
                    (SELECT sum(a.amount) FROM public.sales_invoice_allocation a WHERE a.sales_invoice_id = i.sales_invoice_id), 0)),
                payment_status = CASE
                    WHEN i.paid_amount + coalesce((SELECT sum(a.amount) FROM public.sales_invoice_allocation a
                                                   WHERE a.sales_invoice_id = i.sales_invoice_id), 0) >= i.total_amount THEN 'Paid'
                    WHEN i.paid_amount + coalesce((SELECT sum(a.amount) FROM public.sales_invoice_allocation a
                                                   WHERE a.sales_invoice_id = i.sales_invoice_id), 0) > 0 THEN 'PartiallyPaid'
                    ELSE 'Unpaid' END
            WHERE i.sales_invoice_id = ANY(@Ids)
            """, new { Ids = invoiceIds.Distinct().ToArray() }, transaction, cancellationToken: cancellationToken));

    // =====================================================================
    // Sales invoices
    // =====================================================================

    private const string InvoiceSelect = """
        SELECT i.sales_invoice_id AS Id, i.object_id AS ObjectId, i.invoice_no AS InvoiceNo, i.invoice_date AS InvoiceDate,
               i.sale_type AS SaleType, i.due_date AS DueDate, i.customer_id AS CustomerId, c.supplier_code AS CustomerCode,
               coalesce(c.supplier_name, i.customer_name) AS CustomerName, coalesce(i.customer_mobile, c.mobile) AS CustomerMobile,
               nullif(concat_ws(', ', c.address_line1, c.city), '') AS CustomerAddress,
               i.warehouse_id AS WarehouseId, w.warehouse_name AS WarehouseName,
               i.currency_lookup_data_id AS CurrencyLookupDataId, cur.lookup_data_name AS Currency, i.exchange_rate AS ExchangeRate,
               i.subtotal_amount AS SubtotalAmount, i.discount_amount AS DiscountAmount,
               i.extra_discount_amount AS ExtraDiscountAmount, i.tax_amount AS TaxAmount, i.charge_amount AS ChargeAmount,
               i.round_off_amount AS RoundOffAmount, i.total_amount AS TotalAmount, i.paid_amount AS PaidAmount,
               i.payment_method AS PaymentMethod, i.payment_provider AS PaymentProvider,
               i.payment_reference_no AS PaymentReferenceNo, i.settled_amount AS SettledAmount,
               i.total_amount - i.settled_amount AS DueAmount,
               coalesce((SELECT round(sum(l.unit_cost * l.base_quantity), 4) FROM public.sales_invoice_line l
                         WHERE l.sales_invoice_id = i.sales_invoice_id), 0) AS CostAmount,
               i.status AS Status, i.payment_status AS PaymentStatus,
               (SELECT count(*) FROM public.sales_invoice_line l WHERE l.sales_invoice_id = i.sales_invoice_id)::int AS LineCount,
               i.posted_at AT TIME ZONE 'utc' AS PostedAt, i.posted_by AS PostedBy, i.remarks AS Remarks,
               i.created_at AT TIME ZONE 'utc' AS CreatedAt, i.created_by AS CreatedBy, i.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM public.sales_invoice i
        LEFT JOIN public.supplier c ON c.supplier_id = i.customer_id
        JOIN public.warehouse w ON w.warehouse_id = i.warehouse_id
        JOIN public.lookup_data cur ON cur.lookup_data_id = i.currency_lookup_data_id
        """;

    private const string InvoiceFilter = """
        WHERE i.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR i.invoice_no ILIKE @Pattern OR c.supplier_name ILIKE @Pattern
               OR c.supplier_code ILIKE @Pattern OR c.mobile ILIKE @Pattern OR i.customer_name ILIKE @Pattern
               OR i.customer_mobile ILIKE @Pattern)
          AND (CAST(@Status AS text) IS NULL OR i.status = @Status)
          AND (CAST(@CustomerId AS int) IS NULL OR i.customer_id = @CustomerId)
          AND (CAST(@From AS date) IS NULL OR i.invoice_date >= @From)
          AND (CAST(@To AS date) IS NULL OR i.invoice_date <= @To)
          AND (CAST(@SaleType AS text) IS NULL OR i.sale_type = @SaleType)
          AND (CAST(@PaymentStatus AS text) IS NULL OR i.payment_status = @PaymentStatus)
        """;

    public async Task<PagedResult<SalesInvoice>> GetInvoicesAsync(int organizationId, SalesDocumentQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<SalesInvoice>(connection,
            $"SELECT count(*) FROM public.sales_invoice i LEFT JOIN public.supplier c ON c.supplier_id = i.customer_id {InvoiceFilter}",
            $"{InvoiceSelect} {InvoiceFilter} ORDER BY i.invoice_date DESC, i.sales_invoice_id DESC",
            ListParams(organizationId, query), query.Limit, query.Offset, cancellationToken);
    }

    public async Task<SalesInvoice?> GetInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var invoice = await connection.QuerySingleOrDefaultAsync<SalesInvoice>(new CommandDefinition(
            $"{InvoiceSelect} WHERE i.organization_id = @Org AND i.sales_invoice_id = @Id",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
        if (invoice is null)
            return null;

        invoice.Lines = (await connection.QueryAsync<SalesLine>(new CommandDefinition("""
            SELECT l.sales_invoice_line_id AS Id, l.line_no AS LineNo, l.product_id AS ProductId,
                   p.product_code AS ProductCode, p.product_name AS ProductName, p.product_type AS ProductType,
                   p.tracking_type AS TrackingType, p.has_expiry AS HasExpiry, l.description AS Description,
                   l.unit_id AS UnitId, u.unit_code AS UnitCode, bu.unit_code AS BaseUnitCode,
                   l.conversion_factor AS ConversionFactor, l.quantity AS Quantity, l.base_quantity AS BaseQuantity,
                   l.batch_id AS BatchId, b.batch_no AS BatchNo, b.expiry_date AS ExpiryDate, l.unit_price AS UnitPrice,
                   l.discount_percent AS DiscountPercent, l.discount_amount AS DiscountAmount, l.tax_group_id AS TaxGroupId,
                   tg.tax_group_name AS TaxGroupName, l.tax_amount AS TaxAmount, l.line_total AS LineTotal,
                   l.unit_cost AS UnitCost, l.returned_quantity AS ReturnedQuantity, l.remarks AS Remarks
            FROM public.sales_invoice_line l
            JOIN public.product p ON p.product_id = l.product_id
            JOIN public.unit u ON u.unit_id = l.unit_id
            JOIN public.unit bu ON bu.unit_id = p.base_unit_id
            LEFT JOIN public.tax_group tg ON tg.tax_group_id = l.tax_group_id
            LEFT JOIN public.batch b ON b.batch_id = l.batch_id
            WHERE l.sales_invoice_id = @Id
            ORDER BY l.line_no
            """, new { Id = id }, cancellationToken: cancellationToken))).AsList();
        var lineIds = invoice.Lines.Select(l => l.Id).ToArray();

        var serials = (await connection.QueryAsync<(long LineId, string SerialNo)>(new CommandDefinition("""
            SELECT t.sales_invoice_line_id, s.serial_no
            FROM public.sales_invoice_serial t JOIN public.serial_number s ON s.serial_number_id = t.serial_number_id
            WHERE t.sales_invoice_line_id = ANY(@Ids) ORDER BY s.serial_no
            """, new { Ids = lineIds }, cancellationToken: cancellationToken))).ToLookup(s => s.LineId, s => s.SerialNo);
        var taxes = (await connection.QueryAsync<long, LineTaxItem, (long, LineTaxItem)>(new CommandDefinition("""
            SELECT t.sales_invoice_line_id AS LineId, t.tax_rate_id AS TaxRateId, r.tax_code AS TaxCode,
                   t.taxable_amount AS TaxableAmount, t.tax_amount AS TaxAmount, r.is_recoverable AS IsRecoverable
            FROM public.sales_invoice_line_tax t JOIN public.tax_rate r ON r.tax_rate_id = t.tax_rate_id
            WHERE t.sales_invoice_line_id = ANY(@Ids)
            """, new { Ids = lineIds }, cancellationToken: cancellationToken),
            (lineId, tax) => (lineId, tax), splitOn: "TaxRateId")).ToLookup(t => t.Item1, t => t.Item2);
        foreach (var line in invoice.Lines)
        {
            line.SerialNos = serials[line.Id].ToList();
            line.Taxes = taxes[line.Id].ToList();
        }

        invoice.Allocations = (await connection.QueryAsync<SalesAllocationItem>(new CommandDefinition("""
            SELECT a.invoice_allocation_id AS Id, a.allocation_date AS AllocationDate, a.amount AS Amount,
                   CASE WHEN a.customer_receipt_id IS NOT NULL THEN 'Receipt' ELSE 'Return' END AS SourceType,
                   coalesce(a.customer_receipt_id, a.sales_return_id) AS SourceId,
                   coalesce(r.receipt_no, sr.return_no) AS SourceNo
            FROM public.sales_invoice_allocation a
            LEFT JOIN public.customer_receipt r ON r.customer_receipt_id = a.customer_receipt_id
            LEFT JOIN public.sales_return sr ON sr.sales_return_id = a.sales_return_id
            WHERE a.sales_invoice_id = @Id
            ORDER BY a.allocation_date, a.invoice_allocation_id
            """, new { Id = id }, cancellationToken: cancellationToken))).AsList();
        return invoice;
    }

    /// <summary>The invoice header as posting and cancelling need it, locked for the transaction.</summary>
    private sealed class InvoiceHeader
    {
        public long Id { get; set; }
        public string Number { get; set; } = string.Empty;
        public DateOnly DocumentDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public int WarehouseId { get; set; }
        public int? CustomerId { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal ExchangeRate { get; set; }
    }

    private static async Task<InvoiceHeader> LockInvoiceAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, long id, CancellationToken cancellationToken)
        => await connection.QuerySingleOrDefaultAsync<InvoiceHeader>(new CommandDefinition("""
            SELECT sales_invoice_id AS Id, invoice_no AS Number, invoice_date AS DocumentDate, status AS Status,
                   warehouse_id AS WarehouseId, customer_id AS CustomerId, total_amount AS TotalAmount,
                   paid_amount AS PaidAmount, exchange_rate AS ExchangeRate
            FROM public.sales_invoice WHERE organization_id = @Org AND sales_invoice_id = @Id
            FOR UPDATE
            """, new { Org = organizationId, Id = id }, transaction, cancellationToken: cancellationToken))
           ?? throw new BusinessRuleException("The invoice doesn't exist.");

    public async Task<long> SaveInvoiceAsync(
        int organizationId, SalesInvoice invoice, IReadOnlyDictionary<int, IReadOnlyList<long>> serialIdsByLine, int userId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
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
            invoice.RoundOffAmount,
            invoice.TotalAmount,
            invoice.PaidAmount,
            invoice.PaymentMethod,
            invoice.PaymentProvider,
            invoice.PaymentReferenceNo,
            SettledAmount = invoice.PaidAmount,
            invoice.Remarks,
            UserId = userId
        });

        long id;
        if (invoice.Id == 0)
        {
            parameters.Add("Number", await DocumentNumbers.NextAsync(connection, transaction, organizationId, DocumentType.SalesInvoice, cancellationToken));
            id = await connection.QuerySingleAsync<long>(new CommandDefinition($"""
                INSERT INTO public.sales_invoice (
                    organization_id, invoice_no, invoice_date, sale_type, due_date, customer_id, customer_name,
                    customer_mobile, warehouse_id, currency_lookup_data_id, exchange_rate, subtotal_amount,
                    discount_amount, extra_discount_amount, tax_amount, charge_amount, round_off_amount, total_amount,
                    paid_amount, payment_method, payment_provider, payment_reference_no, settled_amount, status,
                    payment_status, remarks, created_at, created_by)
                VALUES (
                    @Org, @Number, @InvoiceDate, @SaleType, @DueDate, @CustomerId, @CustomerName,
                    @CustomerMobile, @WarehouseId, @CurrencyLookupDataId, @ExchangeRate, @SubtotalAmount,
                    @DiscountAmount, @ExtraDiscountAmount, @TaxAmount, @ChargeAmount, @RoundOffAmount, @TotalAmount,
                    @PaidAmount, @PaymentMethod, @PaymentProvider, @PaymentReferenceNo, @SettledAmount, 'Draft',
                    'Unpaid', @Remarks, {SecurityDb.NowUtc}, @UserId)
                RETURNING sales_invoice_id
                """, parameters, transaction, cancellationToken: cancellationToken));
        }
        else
        {
            id = invoice.Id;
            var header = await LockInvoiceAsync(connection, transaction, organizationId, id, cancellationToken);
            if (header.Status != DocumentStatus.Draft)
                throw new BusinessRuleException("Only a draft invoice can be changed.");
            parameters.Add("Id", id);
            await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE public.sales_invoice
                SET invoice_date = @InvoiceDate, sale_type = @SaleType, due_date = @DueDate, customer_id = @CustomerId,
                    customer_name = @CustomerName, customer_mobile = @CustomerMobile, warehouse_id = @WarehouseId,
                    currency_lookup_data_id = @CurrencyLookupDataId, exchange_rate = @ExchangeRate,
                    subtotal_amount = @SubtotalAmount, discount_amount = @DiscountAmount,
                    extra_discount_amount = @ExtraDiscountAmount, tax_amount = @TaxAmount, charge_amount = @ChargeAmount,
                    round_off_amount = @RoundOffAmount, total_amount = @TotalAmount, paid_amount = @PaidAmount,
                    payment_method = @PaymentMethod, payment_provider = @PaymentProvider,
                    payment_reference_no = @PaymentReferenceNo, settled_amount = @SettledAmount, remarks = @Remarks,
                    updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                WHERE sales_invoice_id = @Id;
                DELETE FROM public.sales_invoice_line WHERE sales_invoice_id = @Id;
                """, parameters, transaction, cancellationToken: cancellationToken));
        }

        foreach (var line in invoice.Lines)
        {
            var lineId = await connection.QuerySingleAsync<long>(new CommandDefinition("""
                INSERT INTO public.sales_invoice_line (
                    sales_invoice_id, line_no, product_id, description, unit_id, conversion_factor, quantity, batch_id,
                    unit_price, discount_percent, discount_amount, tax_group_id, tax_amount, line_total, remarks)
                VALUES (
                    @DocumentId, @LineNo, @ProductId, @Description, @UnitId, @ConversionFactor, @Quantity, @BatchId,
                    @UnitPrice, @DiscountPercent, @DiscountAmount, @TaxGroupId, @TaxAmount, @LineTotal, @Remarks)
                RETURNING sales_invoice_line_id
                """, new
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
                }, transaction, cancellationToken: cancellationToken));

            if (serialIdsByLine.TryGetValue(line.LineNo, out var serialIds) && serialIds.Count > 0)
                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO public.sales_invoice_serial (sales_invoice_line_id, serial_number_id) SELECT @LineId, unnest(@Ids)",
                    new { LineId = lineId, Ids = serialIds.ToArray() }, transaction, cancellationToken: cancellationToken));
            if (line.Taxes.Count > 0)
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO public.sales_invoice_line_tax (sales_invoice_line_id, tax_rate_id, taxable_amount, tax_amount)
                    VALUES (@LineId, @TaxRateId, @TaxableAmount, @TaxAmount)
                    """, line.Taxes.Select(t => new { LineId = lineId, t.TaxRateId, t.TaxableAmount, t.TaxAmount }),
                    transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<DeleteOutcome> DeleteInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var status = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT status FROM public.sales_invoice WHERE organization_id = @Org AND sales_invoice_id = @Id",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
        if (status is null)
            return DeleteOutcome.NotFound;
        if (status != DocumentStatus.Draft)
            return DeleteOutcome.InUse;
        return await RepositoryHelpers.DeleteAsync(connection, new CommandDefinition(
            "DELETE FROM public.sales_invoice WHERE organization_id = @Org AND sales_invoice_id = @Id AND status = 'Draft'",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
    }

    private sealed class SellingLine
    {
        public long Id { get; set; }
        public int LineNo { get; set; }
        public int ProductId { get; set; }
        public string Label { get; set; } = string.Empty;
        public string ProductType { get; set; } = "Stock";
        public string TrackingType { get; set; } = "None";
        public decimal BaseQuantity { get; set; }
        public long? BatchId { get; set; }
        public decimal LastPurchaseCost { get; set; }
    }

    private sealed class BatchCandidate
    {
        public long BatchId { get; set; }
        public string BatchNo { get; set; } = string.Empty;
        public DateOnly? ExpiryDate { get; set; }
        public decimal Quantity { get; set; }
    }

    public async Task PostInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var header = await LockInvoiceAsync(connection, transaction, organizationId, id, cancellationToken);
        if (header.Status != DocumentStatus.Draft)
            throw new BusinessRuleException("Only a draft invoice can be posted.");

        var lines = (await connection.QueryAsync<SellingLine>(new CommandDefinition("""
            SELECT l.sales_invoice_line_id AS Id, l.line_no AS LineNo, l.product_id AS ProductId,
                   p.product_code || ' ' || p.product_name AS Label, p.product_type AS ProductType,
                   p.tracking_type AS TrackingType, l.base_quantity AS BaseQuantity, l.batch_id AS BatchId,
                   p.last_purchase_cost AS LastPurchaseCost
            FROM public.sales_invoice_line l JOIN public.product p ON p.product_id = l.product_id
            WHERE l.sales_invoice_id = @Id ORDER BY l.line_no
            """, new { Id = id }, transaction, cancellationToken: cancellationToken))).AsList();
        if (lines.Count == 0)
            throw new BusinessRuleException("Add at least one line before posting.");

        await CheckCreditAsync(connection, transaction, header, cancellationToken);

        var settings = await PostingSettingsReader.ReadAsync(connection, transaction, organizationId, cancellationToken);
        var serials = (await connection.QueryAsync<(long LineId, long SerialId)>(new CommandDefinition(
            "SELECT sales_invoice_line_id, serial_number_id FROM public.sales_invoice_serial WHERE sales_invoice_line_id = ANY(@Ids)",
            new { Ids = lines.Select(l => l.Id).ToArray() }, transaction, cancellationToken: cancellationToken)))
            .ToLookup(s => s.LineId, s => s.SerialId);

        var stockLines = lines.Where(l => l.ProductType == "Stock").ToList();
        var picked = new Dictionary<(int ProductId, long BatchId), decimal>();
        var moves = new List<StockMove>();
        foreach (var line in stockLines)
        {
            var label = $"Line {line.LineNo} ({line.Label})";
            if (line.TrackingType == "Batch")
            {
                line.BatchId = await PickBatchAsync(connection, transaction, header, line, label, picked, settings.AllowNegativeStock, cancellationToken);
                picked[(line.ProductId, line.BatchId.Value)] = picked.GetValueOrDefault((line.ProductId, line.BatchId.Value)) + line.BaseQuantity;
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE public.sales_invoice_line SET batch_id = @BatchId WHERE sales_invoice_line_id = @Id",
                    new { line.Id, line.BatchId }, transaction, cancellationToken: cancellationToken));
            }

            var serialIds = serials[line.Id].ToList();
            if (line.TrackingType == "Serial")
            {
                if (serialIds.Count != line.BaseQuantity)
                    throw new BusinessRuleException($"{label}: choose one serial number per item sold.");
                var taken = await connection.ExecuteAsync(new CommandDefinition($"""
                    UPDATE public.serial_number SET status = 'Sold', warehouse_id = NULL,
                        updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                    WHERE serial_number_id = ANY(@Ids) AND status = 'InStock' AND warehouse_id = @WarehouseId
                    """, new { Ids = serialIds.ToArray(), header.WarehouseId, UserId = userId }, transaction,
                    cancellationToken: cancellationToken));
                if (taken != serialIds.Count)
                    throw new BusinessRuleException($"{label}: one of the serial numbers isn't in stock in this warehouse any more.");
            }

            moves.Add(new StockMove(header.WarehouseId, line.ProductId, line.BatchId, -line.BaseQuantity, "Sale", line.Id,
                SerialNumberIds: serialIds));
        }

        var applied = await new StockLedger(connection, transaction, organizationId, userId, header.DocumentDate,
            DocumentType.SalesInvoice, id, header.Number, settings.AllowNegativeStock).ApplyAsync(moves, cancellationToken);

        // The cost of goods sold per base unit: the average cost taken out of stock; for items not kept
        // in stock, the last purchase cost (services cost nothing here).
        var costs = stockLines.Select((line, index) => new { line.Id, Cost = applied[index].UnitCost })
            .Concat(lines.Where(l => l.ProductType != "Stock")
                .Select(l => new { l.Id, Cost = l.ProductType == "NonStock" ? l.LastPurchaseCost : 0m }));
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE public.sales_invoice_line SET unit_cost = round(@Cost, 6) WHERE sales_invoice_line_id = @Id",
            costs, transaction, cancellationToken: cancellationToken));

        await SetStatusAsync(connection, transaction, "public.sales_invoice", "sales_invoice_id", id, DocumentStatus.Posted,
            userId, cancellationToken, posted: true);
        await RecomputeSettlementAsync(connection, transaction, [id], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// A credit sale may not take the customer past their credit limit (0 = cash only). The customer row
    /// is locked first, so two counters can't both use the last of the credit.
    /// </summary>
    private static async Task CheckCreditAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, InvoiceHeader header, CancellationToken cancellationToken)
    {
        if (header.CustomerId is null)
            return;
        var unpaid = Math.Round((header.TotalAmount - header.PaidAmount) * header.ExchangeRate, 4, MidpointRounding.AwayFromZero);
        // The draft may name a party that has since stopped being a customer (it stays on as a supplier).
        var (name, limit) = await connection.QuerySingleOrDefaultAsync<(string?, decimal?)>(new CommandDefinition(
            "SELECT supplier_name, customer_credit_limit FROM public.supplier WHERE supplier_id = @Id AND is_customer FOR UPDATE",
            new { Id = header.CustomerId }, transaction, cancellationToken: cancellationToken));
        if (name is null)
            throw new BusinessRuleException("The customer is no longer on file. Choose another customer for this sale.");
        if (unpaid <= 0 || limit is null)
            return;
        if (limit == 0)
            throw new BusinessRuleException($"{name} buys on cash only (credit limit 0). Take the full payment, or raise the limit.");

        var balance = await connection.QuerySingleAsync<decimal>(new CommandDefinition(
            "SELECT coalesce(sum(debit_amount - credit_amount), 0) FROM public.customer_ledger WHERE customer_id = @Id",
            new { Id = header.CustomerId }, transaction, cancellationToken: cancellationToken));
        if (balance + unpaid > limit.Value + 0.0001m)
            throw new BusinessRuleException(
                $"This sale takes {name} past the credit limit: owes {balance:0.##}, limit {limit:0.##}, " +
                $"this sale adds {unpaid:0.##} on credit. Take more payment now, or raise the limit.");
    }

    /// <summary>
    /// The lot a batch-tracked line sells from: the one chosen (it must not be expired), else the first
    /// to expire that still has enough stock after the lines before it.
    /// </summary>
    private static async Task<long> PickBatchAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, InvoiceHeader header, SellingLine line, string label,
        IReadOnlyDictionary<(int, long), decimal> picked, bool allowNegativeStock, CancellationToken cancellationToken)
    {
        var candidates = (await connection.QueryAsync<BatchCandidate>(new CommandDefinition("""
            SELECT b.batch_id AS BatchId, b.batch_no AS BatchNo, b.expiry_date AS ExpiryDate,
                   coalesce(sb.quantity, 0) AS Quantity
            FROM public.batch b
            LEFT JOIN public.stock_balance sb
                   ON sb.batch_id = b.batch_id AND sb.warehouse_id = @WarehouseId AND sb.product_id = b.product_id
            WHERE b.product_id = @ProductId AND (CAST(@BatchId AS bigint) IS NULL OR b.batch_id = @BatchId)
              AND (CAST(@BatchId AS bigint) IS NOT NULL OR sb.quantity > 0)
            ORDER BY b.expiry_date NULLS LAST, b.batch_id
            """, new { header.WarehouseId, line.ProductId, line.BatchId }, transaction, cancellationToken: cancellationToken))).AsList();

        if (line.BatchId is not null)
        {
            var chosen = candidates.FirstOrDefault()
                ?? throw new BusinessRuleException($"{label}: the batch isn't one of this product's batches.");
            if (chosen.ExpiryDate is { } expiry && expiry < header.DocumentDate)
                throw new BusinessRuleException($"{label}: batch {chosen.BatchNo} expired on {expiry:dd MMM yyyy} and can't be sold.");
            return chosen.BatchId;
        }

        var unexpired = candidates.Where(c => c.ExpiryDate is null || c.ExpiryDate >= header.DocumentDate).ToList();
        var fit = unexpired.FirstOrDefault(c => c.Quantity - picked.GetValueOrDefault((line.ProductId, c.BatchId)) >= line.BaseQuantity);
        if (fit is not null)
            return fit.BatchId;
        if (allowNegativeStock && unexpired.Count > 0)
            return unexpired[0].BatchId;
        throw new BusinessRuleException(unexpired.Count == 0
            ? $"{label}: no unexpired batch is in stock in this warehouse."
            : $"{label}: no single batch has {line.BaseQuantity:0.####} in stock. Choose the batches, splitting the line.");
    }

    public async Task CancelInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var header = await LockInvoiceAsync(connection, transaction, organizationId, id, cancellationToken);
        if (header.Status != DocumentStatus.Posted)
            throw new BusinessRuleException(header.Status == DocumentStatus.Draft
                ? "Delete a draft invoice instead of cancelling it."
                : "This invoice is already cancelled.");

        var blockers = await connection.QuerySingleAsync<(int Allocations, int Returns)>(new CommandDefinition("""
            SELECT (SELECT count(*) FROM public.sales_invoice_allocation WHERE sales_invoice_id = @Id)::int,
                   (SELECT count(*) FROM public.sales_return WHERE sales_invoice_id = @Id AND status = 'Posted')::int
            """, new { Id = id }, transaction, cancellationToken: cancellationToken));
        if (blockers.Allocations > 0)
            throw new BusinessRuleException("Customer receipts settle this invoice. Cancel them first.");
        if (blockers.Returns > 0)
            throw new BusinessRuleException("Goods from this invoice were returned. Cancel the returns first.");

        var serialIds = await StockItems.MovedSerialsAsync(connection, transaction, organizationId, DocumentType.SalesInvoice, id, cancellationToken);
        await StockItems.RestoreSerialsAsync(connection, transaction, userId, serialIds, header.WarehouseId, cancellationToken);
        var settings = await PostingSettingsReader.ReadAsync(connection, transaction, organizationId, cancellationToken);
        await new StockLedger(connection, transaction, organizationId, userId, DateOnly.FromDateTime(DateTime.UtcNow),
            DocumentType.SalesInvoice, id, header.Number, settings.AllowNegativeStock).ReverseAsync(null, cancellationToken);
        await SetStatusAsync(connection, transaction, "public.sales_invoice", "sales_invoice_id", id, DocumentStatus.Cancelled,
            userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OpenSalesInvoice>> GetOpenInvoicesAsync(int organizationId, int customerId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<OpenSalesInvoice>(new CommandDefinition("""
            SELECT sales_invoice_id AS Id, invoice_no AS InvoiceNo, invoice_date AS InvoiceDate, due_date AS DueDate,
                   currency_lookup_data_id AS CurrencyLookupDataId, total_amount AS TotalAmount,
                   settled_amount AS SettledAmount, total_amount - settled_amount AS DueAmount
            FROM public.sales_invoice
            WHERE organization_id = @Org AND customer_id = @CustomerId AND status = 'Posted' AND payment_status <> 'Paid'
            ORDER BY coalesce(due_date, invoice_date), invoice_date, sales_invoice_id
            """, new { Org = organizationId, CustomerId = customerId }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyDictionary<string, long>> FindSerialsInStockAsync(
        int organizationId, int productId, int warehouseId, IReadOnlyCollection<string> serialNos, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<(string SerialNo, long Id)>(new CommandDefinition("""
            SELECT serial_no, serial_number_id FROM public.serial_number
            WHERE organization_id = @Org AND product_id = @ProductId AND warehouse_id = @WarehouseId AND status = 'InStock'
              AND serial_no = ANY(@SerialNos)
            """, new { Org = organizationId, ProductId = productId, WarehouseId = warehouseId, SerialNos = serialNos.ToArray() },
            cancellationToken: cancellationToken))).ToDictionary(s => s.SerialNo, s => s.Id, StringComparer.Ordinal);
    }

    // =====================================================================
    // Customer receipts (recorded and posted in one step)
    // =====================================================================

    private const string ReceiptSelect = """
        SELECT r.customer_receipt_id AS Id, r.object_id AS ObjectId, r.receipt_no AS ReceiptNo, r.receipt_date AS ReceiptDate,
               r.customer_id AS CustomerId, c.supplier_code AS CustomerCode, c.supplier_name AS CustomerName,
               c.mobile AS CustomerMobile, r.receipt_type AS ReceiptType, r.payment_method AS PaymentMethod,
               r.payment_provider AS PaymentProvider, r.reference_no AS ReferenceNo, r.cheque_date AS ChequeDate,
               r.cheque_status AS ChequeStatus, r.currency_lookup_data_id AS CurrencyLookupDataId,
               cur.lookup_data_name AS Currency, r.exchange_rate AS ExchangeRate, r.amount AS Amount,
               r.tax_deducted_amount AS TaxDeductedAmount, r.allocated_amount AS AllocatedAmount,
               r.amount + r.tax_deducted_amount - r.allocated_amount AS UnallocatedAmount, r.status AS Status,
               r.posted_at AT TIME ZONE 'utc' AS PostedAt, r.posted_by AS PostedBy, r.remarks AS Remarks,
               r.created_at AT TIME ZONE 'utc' AS CreatedAt, r.created_by AS CreatedBy, r.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM public.customer_receipt r
        JOIN public.supplier c ON c.supplier_id = r.customer_id
        JOIN public.lookup_data cur ON cur.lookup_data_id = r.currency_lookup_data_id
        """;

    private const string ReceiptFilter = """
        WHERE r.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR r.receipt_no ILIKE @Pattern OR c.supplier_name ILIKE @Pattern
               OR c.supplier_code ILIKE @Pattern OR c.mobile ILIKE @Pattern OR r.reference_no ILIKE @Pattern)
          AND (CAST(@Status AS text) IS NULL OR r.status = @Status)
          AND (CAST(@CustomerId AS int) IS NULL OR r.customer_id = @CustomerId)
          AND (CAST(@From AS date) IS NULL OR r.receipt_date >= @From)
          AND (CAST(@To AS date) IS NULL OR r.receipt_date <= @To)
        """;

    public async Task<PagedResult<CustomerReceipt>> GetReceiptsAsync(int organizationId, SalesDocumentQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<CustomerReceipt>(connection,
            $"SELECT count(*) FROM public.customer_receipt r JOIN public.supplier c ON c.supplier_id = r.customer_id {ReceiptFilter}",
            $"{ReceiptSelect} {ReceiptFilter} ORDER BY r.receipt_date DESC, r.customer_receipt_id DESC",
            ListParams(organizationId, query), query.Limit, query.Offset, cancellationToken);
    }

    public async Task<CustomerReceipt?> GetReceiptAsync(int organizationId, long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var receipt = await connection.QuerySingleOrDefaultAsync<CustomerReceipt>(new CommandDefinition(
            $"{ReceiptSelect} WHERE r.organization_id = @Org AND r.customer_receipt_id = @Id",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
        if (receipt is null)
            return null;
        receipt.Allocations = (await connection.QueryAsync<ReceiptAllocationItem>(new CommandDefinition("""
            SELECT a.sales_invoice_id AS SalesInvoiceId, i.invoice_no AS InvoiceNo, i.invoice_date AS InvoiceDate,
                   i.total_amount AS InvoiceTotal, a.amount AS Amount
            FROM public.sales_invoice_allocation a JOIN public.sales_invoice i ON i.sales_invoice_id = a.sales_invoice_id
            WHERE a.customer_receipt_id = @Id ORDER BY i.invoice_date, i.sales_invoice_id
            """, new { Id = id }, cancellationToken: cancellationToken))).AsList();
        return receipt;
    }

    public async Task<long> CreateReceiptAsync(int organizationId, CustomerReceipt receipt, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Re-check what each invoice still owes under lock, so two receipts can't both settle the same due.
        foreach (var allocation in receipt.Allocations)
        {
            var due = await connection.QuerySingleOrDefaultAsync<decimal?>(new CommandDefinition("""
                SELECT total_amount - settled_amount FROM public.sales_invoice
                WHERE organization_id = @Org AND sales_invoice_id = @Id AND customer_id = @CustomerId
                  AND status = 'Posted' AND currency_lookup_data_id = @CurrencyId
                FOR UPDATE
                """, new { Org = organizationId, Id = allocation.SalesInvoiceId, receipt.CustomerId, CurrencyId = receipt.CurrencyLookupDataId },
                transaction, cancellationToken: cancellationToken));
            if (due is null)
                throw new BusinessRuleException("A receipt can only settle this customer's posted invoices in the same currency.");
            if (allocation.Amount > due.Value + 0.0001m)
                throw new BusinessRuleException($"Invoice {allocation.InvoiceNo} has only {due.Value:0.##} left to collect.");
        }

        var number = await DocumentNumbers.NextAsync(connection, transaction, organizationId, DocumentType.CustomerReceipt, cancellationToken);
        var id = await connection.QuerySingleAsync<long>(new CommandDefinition($"""
            INSERT INTO public.customer_receipt (
                organization_id, receipt_no, receipt_date, customer_id, receipt_type, payment_method, payment_provider,
                reference_no, cheque_date, cheque_status, currency_lookup_data_id, exchange_rate, amount, tax_deducted_amount,
                allocated_amount, status, posted_at, posted_by, remarks, created_at, created_by)
            VALUES (
                @Org, @Number, @ReceiptDate, @CustomerId, @ReceiptType, @PaymentMethod, @PaymentProvider,
                @ReferenceNo, @ChequeDate, @ChequeStatus, @CurrencyLookupDataId, @ExchangeRate, @Amount, @TaxDeductedAmount,
                @AllocatedAmount, 'Posted', {SecurityDb.NowUtc}, @UserId, @Remarks, {SecurityDb.NowUtc}, @UserId)
            RETURNING customer_receipt_id
            """, new
            {
                Org = organizationId,
                Number = number,
                receipt.ReceiptDate,
                receipt.CustomerId,
                receipt.ReceiptType,
                receipt.PaymentMethod,
                receipt.PaymentProvider,
                receipt.ReferenceNo,
                receipt.ChequeDate,
                receipt.ChequeStatus,
                receipt.CurrencyLookupDataId,
                receipt.ExchangeRate,
                receipt.Amount,
                receipt.TaxDeductedAmount,
                AllocatedAmount = receipt.Allocations.Sum(a => a.Amount),
                receipt.Remarks,
                UserId = userId
            }, transaction, cancellationToken: cancellationToken));

        if (receipt.Allocations.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition($"""
                INSERT INTO public.sales_invoice_allocation (organization_id, sales_invoice_id, customer_receipt_id, allocation_date,
                                                      amount, created_at, created_by)
                VALUES (@Org, @InvoiceId, @ReceiptId, @Date, @Amount, {SecurityDb.NowUtc}, @UserId)
                """, receipt.Allocations.Select(a => new
                {
                    Org = organizationId,
                    InvoiceId = a.SalesInvoiceId,
                    ReceiptId = id,
                    Date = receipt.ReceiptDate,
                    a.Amount,
                    UserId = userId
                }), transaction, cancellationToken: cancellationToken));
            await RecomputeSettlementAsync(connection, transaction, receipt.Allocations.Select(a => a.SalesInvoiceId), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    private sealed class ReceiptHeader
    {
        public string Status { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
        public string? ChequeStatus { get; set; }
    }

    private static async Task<ReceiptHeader> LockReceiptAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, long id, CancellationToken cancellationToken)
        => await connection.QuerySingleOrDefaultAsync<ReceiptHeader>(new CommandDefinition("""
            SELECT status AS Status, payment_method AS PaymentMethod, cheque_status AS ChequeStatus
            FROM public.customer_receipt WHERE organization_id = @Org AND customer_receipt_id = @Id
            FOR UPDATE
            """, new { Org = organizationId, Id = id }, transaction, cancellationToken: cancellationToken))
           ?? throw new BusinessRuleException("The receipt doesn't exist.");

    /// <summary>Takes a receipt off the invoices it settled.</summary>
    private static async Task UnallocateReceiptAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long id, int userId, CancellationToken cancellationToken)
    {
        var invoices = (await connection.QueryAsync<long>(new CommandDefinition(
            "DELETE FROM public.sales_invoice_allocation WHERE customer_receipt_id = @Id RETURNING sales_invoice_id",
            new { Id = id }, transaction, cancellationToken: cancellationToken))).AsList();
        await RecomputeSettlementAsync(connection, transaction, invoices, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.customer_receipt SET allocated_amount = 0, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE customer_receipt_id = @Id
            """, new { Id = id, UserId = userId }, transaction, cancellationToken: cancellationToken));
    }

    public async Task CancelReceiptAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var header = await LockReceiptAsync(connection, transaction, organizationId, id, cancellationToken);
        if (header.Status != DocumentStatus.Posted)
            throw new BusinessRuleException("This receipt is already cancelled.");
        await UnallocateReceiptAsync(connection, transaction, id, userId, cancellationToken);
        await SetStatusAsync(connection, transaction, "public.customer_receipt", "customer_receipt_id", id,
            DocumentStatus.Cancelled, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetChequeStatusAsync(int organizationId, long id, string chequeStatus, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var header = await LockReceiptAsync(connection, transaction, organizationId, id, cancellationToken);
        if (header.Status != DocumentStatus.Posted)
            throw new BusinessRuleException("Only a posted receipt's cheque can change.");
        if (header.PaymentMethod != PaymentMethod.Cheque)
            throw new BusinessRuleException("This receipt wasn't paid by cheque.");
        if (header.ChequeStatus == "Bounced")
            throw new BusinessRuleException("A bounced cheque stays bounced; record a new receipt instead.");

        // A bounced cheque didn't pay anything: the invoices it settled are due again.
        if (chequeStatus == "Bounced")
            await UnallocateReceiptAsync(connection, transaction, id, userId, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.customer_receipt SET cheque_status = @Status, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE customer_receipt_id = @Id
            """, new { Id = id, Status = chequeStatus, UserId = userId }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }
}
