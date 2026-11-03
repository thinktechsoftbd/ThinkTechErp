using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Purchase;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Purchase;

public interface IPurchaseDocumentRepository
{
    Task<PagedResult<PurchaseInvoice>> GetInvoicesAsync(int organizationId, PurchaseDocumentQuery query, CancellationToken cancellationToken = default);
    Task<PurchaseInvoice?> GetInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    /// <summary>Creates (Id 0) or replaces a draft invoice with fully calculated lines; returns its id.</summary>
    Task<long> SaveInvoiceAsync(int organizationId, PurchaseInvoice invoice, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task PostInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task CancelInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OpenPurchaseInvoice>> GetOpenInvoicesAsync(int organizationId, int supplierId, CancellationToken cancellationToken = default);
    Task<bool> SupplierInvoiceNoExistsAsync(int organizationId, int supplierId, string supplierInvoiceNo, long? excludeId, CancellationToken cancellationToken = default);

    Task<PagedResult<PurchaseOrder>> GetOrdersAsync(int organizationId, PurchaseDocumentQuery query, CancellationToken cancellationToken = default);
    Task<PurchaseOrder?> GetOrderAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<long> SaveOrderAsync(int organizationId, PurchaseOrder order, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteOrderAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    /// <summary>Approve, Close or Cancel.</summary>
    Task ChangeOrderStatusAsync(int organizationId, long id, string action, int userId, CancellationToken cancellationToken = default);

    Task<PagedResult<GoodsReceipt>> GetReceiptsAsync(int organizationId, PurchaseDocumentQuery query, CancellationToken cancellationToken = default);
    Task<GoodsReceipt?> GetReceiptAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<long> SaveReceiptAsync(int organizationId, GoodsReceipt receipt, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteReceiptAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task PostReceiptAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task CancelReceiptAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);

    Task<PagedResult<PurchaseReturn>> GetReturnsAsync(int organizationId, PurchaseDocumentQuery query, CancellationToken cancellationToken = default);
    Task<PurchaseReturn?> GetReturnAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<long> SaveReturnAsync(int organizationId, PurchaseReturn purchaseReturn, IReadOnlyDictionary<int, IReadOnlyList<long>> serialIdsByLine, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteReturnAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task PostReturnAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task CancelReturnAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    /// <summary>serial_number ids of a product's items in stock in a warehouse, by serial number.</summary>
    Task<IReadOnlyDictionary<string, long>> FindSerialsInStockAsync(int organizationId, int productId, int warehouseId, IReadOnlyCollection<string> serialNos, CancellationToken cancellationToken = default);

    Task<PagedResult<SupplierPayment>> GetPaymentsAsync(int organizationId, PurchaseDocumentQuery query, CancellationToken cancellationToken = default);
    Task<SupplierPayment?> GetPaymentAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    /// <summary>Records and posts a payment with its allocations in one step; returns its id.</summary>
    Task<long> CreatePaymentAsync(int organizationId, SupplierPayment payment, int userId, CancellationToken cancellationToken = default);
    Task CancelPaymentAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task SetChequeStatusAsync(int organizationId, long id, string chequeStatus, int userId, CancellationToken cancellationToken = default);
}

public sealed partial class PurchaseDocumentRepository(IDbConnectionFactory connectionFactory) : IPurchaseDocumentRepository
{
    private sealed class LockedHeader
    {
        public long Id { get; set; }
        public string Number { get; set; } = string.Empty;
        public DateOnly DocumentDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public int? WarehouseId { get; set; }
        public int? SupplierId { get; set; }
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<LockedHeader> LockAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string table, string key, string numberColumn,
        string dateColumn, string warehouseColumn, int organizationId, long id, string label, CancellationToken cancellationToken)
        => await connection.QuerySingleOrDefaultAsync<LockedHeader>(new CommandDefinition($"""
            SELECT {key} AS Id, {numberColumn} AS Number, {dateColumn} AS DocumentDate, status AS Status,
                   {warehouseColumn} AS WarehouseId, supplier_id AS SupplierId
            FROM {table} WHERE organization_id = @Org AND {key} = @Id
            FOR UPDATE
            """, new { Org = organizationId, Id = id }, transaction, cancellationToken: cancellationToken))
           ?? throw new BusinessRuleException($"The {label} doesn't exist.");

    private static void RequireStatus(LockedHeader header, string status, string message)
    {
        if (header.Status != status)
            throw new BusinessRuleException(message);
    }

    private async Task<DeleteOutcome> DeleteDraftAsync(string table, string key, int organizationId, long id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        var status = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            $"SELECT status FROM {table} WHERE organization_id = @Org AND {key} = @Id", new { Org = organizationId, Id = id },
            cancellationToken: cancellationToken));
        if (status is null)
            return DeleteOutcome.NotFound;
        if (status != DocumentStatus.Draft)
            return DeleteOutcome.InUse;
        return await RepositoryHelpers.DeleteAsync(connection, new CommandDefinition(
            $"DELETE FROM {table} WHERE organization_id = @Org AND {key} = @Id AND status = 'Draft'",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
    }

    private static Task SetStatusAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string table, string key, long id, string status,
        int userId, CancellationToken cancellationToken, bool posted = false)
        => connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE {table} SET status = @Status, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                {(posted ? $", posted_at = {SecurityDb.NowUtc}, posted_by = @UserId" : "")}
            WHERE {key} = @Id
            """, new { Id = id, Status = status, UserId = userId }, transaction, cancellationToken: cancellationToken));

    private static object ListParams(int organizationId, PurchaseDocumentQuery query) => new
    {
        Org = organizationId,
        Pattern = RepositoryHelpers.Pattern(query.Search),
        query.Status,
        query.SupplierId,
        query.From,
        query.To,
        query.PurchaseType,
        query.PaymentStatus
    };

    /// <summary>Sets settled_amount (paid on the spot + allocations) and payment_status of these invoices.</summary>
    private static Task RecomputeSettlementAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IEnumerable<long> invoiceIds, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition("""
            UPDATE public.purchase_invoice i
            SET settled_amount = least(i.total_amount, i.paid_amount + coalesce(
                    (SELECT sum(a.amount) FROM public.purchase_invoice_allocation a WHERE a.purchase_invoice_id = i.purchase_invoice_id), 0)),
                payment_status = CASE
                    WHEN i.paid_amount + coalesce((SELECT sum(a.amount) FROM public.purchase_invoice_allocation a
                                                   WHERE a.purchase_invoice_id = i.purchase_invoice_id), 0) >= i.total_amount THEN 'Paid'
                    WHEN i.paid_amount + coalesce((SELECT sum(a.amount) FROM public.purchase_invoice_allocation a
                                                   WHERE a.purchase_invoice_id = i.purchase_invoice_id), 0) > 0 THEN 'PartiallyPaid'
                    ELSE 'Unpaid' END
            WHERE i.purchase_invoice_id = ANY(@Ids)
            """, new { Ids = invoiceIds.Distinct().ToArray() }, transaction, cancellationToken: cancellationToken));

    /// <summary>Received / PartiallyReceived from the order lines' received quantities.</summary>
    private static Task RecomputeOrderStatusAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long? orderId, CancellationToken cancellationToken)
        => orderId is null
            ? Task.CompletedTask
            : connection.ExecuteAsync(new CommandDefinition("""
                UPDATE public.purchase_order o
                SET status = CASE
                    WHEN NOT EXISTS (SELECT 1 FROM public.purchase_order_line l
                                     WHERE l.purchase_order_id = o.purchase_order_id AND l.received_quantity < l.quantity) THEN 'Received'
                    WHEN EXISTS (SELECT 1 FROM public.purchase_order_line l
                                 WHERE l.purchase_order_id = o.purchase_order_id AND l.received_quantity > 0) THEN 'PartiallyReceived'
                    ELSE 'Approved' END
                WHERE o.purchase_order_id = @Id AND o.status IN ('Approved', 'PartiallyReceived', 'Received')
                """, new { Id = orderId }, transaction, cancellationToken: cancellationToken));

    /// <summary>Adds base units received or billed to purchase order lines (negative to undo).</summary>
    private static async Task AddToOrderLinesAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IEnumerable<(long OrderLineId, decimal BaseQuantity)> lines,
        bool received, bool invoiced, CancellationToken cancellationToken)
    {
        foreach (var (orderLineId, baseQuantity) in lines)
            await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE public.purchase_order_line
                SET {(received ? "received_quantity = greatest(0, received_quantity + @Base / conversion_factor)" : "received_quantity = received_quantity")}
                    {(invoiced ? ", invoiced_quantity = greatest(0, invoiced_quantity + @Base / conversion_factor)" : "")}
                WHERE purchase_order_line_id = @Id
                """, new { Id = orderLineId, Base = baseQuantity }, transaction, cancellationToken: cancellationToken));
    }

    private const string ProductColumns = """
        p.product_code AS ProductCode, p.product_name AS ProductName, p.product_type AS ProductType,
        p.tracking_type AS TrackingType, p.has_expiry AS HasExpiry, u.unit_code AS UnitCode, bu.unit_code AS BaseUnitCode
        """;

    private const string ProductJoins = """
        JOIN public.product p ON p.product_id = l.product_id
        JOIN public.unit u ON u.unit_id = l.unit_id
        JOIN public.unit bu ON bu.unit_id = p.base_unit_id
        """;

    private static async Task AttachSerialNosAsync(
        System.Data.IDbConnection connection, string table, string lineColumn, IReadOnlyList<PurchaseLine> lines,
        CancellationToken cancellationToken, bool viaSerialNumber = false)
    {
        if (lines.Count == 0)
            return;
        var sql = viaSerialNumber
            ? $"SELECT t.{lineColumn}, s.serial_no FROM {table} t JOIN public.serial_number s ON s.serial_number_id = t.serial_number_id WHERE t.{lineColumn} = ANY(@Ids) ORDER BY s.serial_no"
            : $"SELECT {lineColumn}, serial_no FROM {table} WHERE {lineColumn} = ANY(@Ids) ORDER BY serial_no";
        var serials = (await connection.QueryAsync<(long LineId, string SerialNo)>(new CommandDefinition(sql,
            new { Ids = lines.Select(l => l.Id).ToArray() }, cancellationToken: cancellationToken))).ToLookup(s => s.LineId, s => s.SerialNo);
        foreach (var line in lines)
            line.SerialNos = serials[line.Id].ToList();
    }

    private static async Task<List<PurchaseCharge>> ChargesAsync(
        System.Data.IDbConnection connection, string column, long id, int? supplierId, CancellationToken cancellationToken)
        => (await connection.QueryAsync<PurchaseCharge>(new CommandDefinition($"""
            SELECT c.purchase_charge_id AS Id, c.charge_name AS ChargeName, c.amount AS Amount,
                   c.allocation_method AS AllocationMethod, c.payee_supplier_id AS PayeeSupplierId,
                   s.supplier_name AS PayeeName,
                   (c.payee_supplier_id IS NOT NULL AND c.payee_supplier_id IS NOT DISTINCT FROM @SupplierId) AS OnSupplierBill
            FROM public.purchase_charge c LEFT JOIN public.supplier s ON s.supplier_id = c.payee_supplier_id
            WHERE c.{column} = @Id ORDER BY c.purchase_charge_id
            """, new { Id = id, SupplierId = supplierId }, cancellationToken: cancellationToken))).AsList();

    private static async Task InsertChargesAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string column, long id, IEnumerable<PurchaseCharge> charges,
        int userId, CancellationToken cancellationToken)
    {
        foreach (var charge in charges)
            await connection.ExecuteAsync(new CommandDefinition($"""
                INSERT INTO public.purchase_charge ({column}, charge_name, amount, allocation_method, payee_supplier_id,
                                                      created_at, created_by)
                VALUES (@Id, @ChargeName, @Amount, @AllocationMethod, @PayeeSupplierId, {SecurityDb.NowUtc}, @UserId)
                """, new { Id = id, charge.ChargeName, charge.Amount, charge.AllocationMethod, charge.PayeeSupplierId, UserId = userId },
                transaction, cancellationToken: cancellationToken));
    }

    private static async Task InsertSerialNosAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string table, string lineColumn, long lineId,
        IReadOnlyCollection<string> serialNos, CancellationToken cancellationToken)
    {
        if (serialNos.Count > 0)
            await connection.ExecuteAsync(new CommandDefinition(
                $"INSERT INTO {table} ({lineColumn}, serial_no) SELECT @LineId, unnest(@SerialNos)",
                new { LineId = lineId, SerialNos = serialNos.ToArray() }, transaction, cancellationToken: cancellationToken));
    }

    /// <summary>A stock line being received: its batch (created on first use) and serial numbers.</summary>
    private sealed class ReceivingLine
    {
        public long Id { get; set; }
        public int ProductId { get; set; }
        public string ProductType { get; set; } = "Stock";
        public string TrackingType { get; set; } = "None";
        public decimal BaseQuantity { get; set; }
        public decimal Quantity { get; set; }
        public decimal ConversionFactor { get; set; }
        public long? BatchId { get; set; }
        public string? BatchNo { get; set; }
        public DateOnly? ManufactureDate { get; set; }
        public DateOnly? ExpiryDate { get; set; }
        public decimal LandedUnitCost { get; set; }
        public long? PurchaseOrderLineId { get; set; }
        public long? GoodsReceiptLineId { get; set; }
    }

    /// <summary>Posts the stock of a direct invoice or goods receipt and returns the moves applied.</summary>
    private static async Task ReceiveStockAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, int userId, LockedHeader header,
        string documentType, IReadOnlyList<ReceivingLine> lines, string serialTable, string lineTable, string lineKey,
        CancellationToken cancellationToken)
    {
        var warehouseId = header.WarehouseId ?? throw new BusinessRuleException("Choose the warehouse the stock goes into.");
        var settings = await PostingSettingsReader.ReadAsync(connection, transaction, organizationId, cancellationToken);
        var serials = (await connection.QueryAsync<(long LineId, string SerialNo)>(new CommandDefinition(
            $"SELECT {lineKey}, serial_no FROM {serialTable} WHERE {lineKey} = ANY(@Ids)",
            new { Ids = lines.Select(l => l.Id).ToArray() }, transaction, cancellationToken: cancellationToken)))
            .ToLookup(s => s.LineId, s => s.SerialNo);

        var moves = new List<StockMove>();
        foreach (var line in lines.Where(l => l.ProductType == "Stock"))
        {
            long? batchId = null;
            if (line.TrackingType == "Batch")
            {
                if (string.IsNullOrWhiteSpace(line.BatchNo) && line.BatchId is null)
                    throw new BusinessRuleException("Enter the batch number for every batch-tracked line.");
                batchId = line.BatchId ?? await StockItems.EnsureBatchAsync(connection, transaction, organizationId, userId,
                    line.ProductId, line.BatchNo!, line.ManufactureDate, line.ExpiryDate, cancellationToken);
                await connection.ExecuteAsync(new CommandDefinition(
                    $"UPDATE {lineTable} SET batch_id = @BatchId WHERE {lineKey} = @Id", new { Id = line.Id, BatchId = batchId },
                    transaction, cancellationToken: cancellationToken));
            }

            IReadOnlyList<long> serialIds = [];
            if (line.TrackingType == "Serial")
            {
                var lineSerials = serials[line.Id].ToList();
                if (lineSerials.Count != line.BaseQuantity)
                    throw new BusinessRuleException($"Enter one serial number per item ({line.BaseQuantity:0.####}) on every serial-tracked line.");
                serialIds = await StockItems.ReceiveSerialsAsync(connection, transaction, organizationId, userId, line.ProductId,
                    batchId, warehouseId, lineSerials, cancellationToken);
                await connection.ExecuteAsync(new CommandDefinition($"""
                    UPDATE {serialTable} t SET serial_number_id = s.serial_number_id
                    FROM public.serial_number s
                    WHERE t.{lineKey} = @LineId AND s.serial_number_id = ANY(@Ids) AND s.serial_no = t.serial_no
                    """, new { LineId = line.Id, Ids = serialIds.ToArray() }, transaction, cancellationToken: cancellationToken));
            }

            moves.Add(new StockMove(warehouseId, line.ProductId, batchId, line.BaseQuantity, "PurchaseReceipt", line.Id,
                UnitCost: line.LandedUnitCost, SerialNumberIds: serialIds));
        }

        await new StockLedger(connection, transaction, organizationId, userId, header.DocumentDate, documentType, header.Id,
            header.Number, settings.AllowNegativeStock).ApplyAsync(moves, cancellationToken);

        // The latest landed cost per base unit, shown on the product and used for count valuation.
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE public.product SET last_purchase_cost = @Cost WHERE product_id = @ProductId
            """, lines.Where(l => l.ProductType == "Stock").Select(l => new { l.ProductId, Cost = l.LandedUnitCost }),
            transaction, cancellationToken: cancellationToken));
    }

    /// <summary>On cancelling a receipt: its serial-tracked items must still be in stock, and leave it.</summary>
    private static async Task RemoveReceivedSerialsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, int userId, string documentType,
        long documentId, int warehouseId, CancellationToken cancellationToken)
    {
        var serialIds = await StockItems.MovedSerialsAsync(connection, transaction, organizationId, documentType, documentId, cancellationToken);
        if (serialIds.Count == 0)
            return;
        var taken = await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.serial_number SET status = 'Scrapped', warehouse_id = NULL,
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE serial_number_id = ANY(@Ids) AND status = 'InStock' AND warehouse_id = @WarehouseId
            """, new { Ids = serialIds.ToArray(), WarehouseId = warehouseId, UserId = userId }, transaction,
            cancellationToken: cancellationToken));
        if (taken != serialIds.Count)
            throw new BusinessRuleException("Some serial-tracked items from this document have already left the stock.");
    }

    // =====================================================================
    // Supplier invoices
    // =====================================================================

    private const string InvoiceSelect = """
        SELECT i.purchase_invoice_id AS Id, i.object_id AS ObjectId, i.invoice_no AS InvoiceNo, i.invoice_date AS InvoiceDate,
               i.purchase_type AS PurchaseType, i.due_date AS DueDate, i.supplier_id AS SupplierId,
               coalesce(s.supplier_name, i.supplier_name) AS SupplierName, coalesce(i.supplier_mobile, s.mobile) AS SupplierMobile,
               i.receives_stock AS ReceivesStock, i.warehouse_id AS WarehouseId, w.warehouse_name AS WarehouseName,
               i.purchase_order_id AS PurchaseOrderId, po.order_no AS PurchaseOrderNo,
               i.supplier_invoice_no AS SupplierInvoiceNo, i.supplier_invoice_date AS SupplierInvoiceDate,
               i.tax_invoice_no AS TaxInvoiceNo, i.currency_lookup_data_id AS CurrencyLookupDataId, cur.lookup_data_name AS Currency,
               i.exchange_rate AS ExchangeRate, i.subtotal_amount AS SubtotalAmount, i.discount_amount AS DiscountAmount,
               i.tax_amount AS TaxAmount, i.charge_amount AS ChargeAmount, i.round_off_amount AS RoundOffAmount,
               i.total_amount AS TotalAmount, i.paid_amount AS PaidAmount, i.payment_method AS PaymentMethod,
               i.payment_provider AS PaymentProvider, i.payment_reference_no AS PaymentReferenceNo,
               i.settled_amount AS SettledAmount, i.total_amount - i.settled_amount AS DueAmount,
               i.status AS Status, i.payment_status AS PaymentStatus,
               (SELECT count(*) FROM public.purchase_invoice_line l WHERE l.purchase_invoice_id = i.purchase_invoice_id)::int AS LineCount,
               i.posted_at AT TIME ZONE 'utc' AS PostedAt, i.posted_by AS PostedBy, i.remarks AS Remarks,
               i.created_at AT TIME ZONE 'utc' AS CreatedAt, i.created_by AS CreatedBy, i.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM public.purchase_invoice i
        LEFT JOIN public.supplier s ON s.supplier_id = i.supplier_id
        LEFT JOIN public.warehouse w ON w.warehouse_id = i.warehouse_id
        LEFT JOIN public.purchase_order po ON po.purchase_order_id = i.purchase_order_id
        JOIN public.lookup_data cur ON cur.lookup_data_id = i.currency_lookup_data_id
        """;

    private const string InvoiceFilter = """
        WHERE i.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR i.invoice_no ILIKE @Pattern OR i.supplier_invoice_no ILIKE @Pattern
               OR s.supplier_name ILIKE @Pattern OR i.supplier_name ILIKE @Pattern OR i.supplier_mobile ILIKE @Pattern)
          AND (CAST(@Status AS text) IS NULL OR i.status = @Status)
          AND (CAST(@SupplierId AS int) IS NULL OR i.supplier_id = @SupplierId)
          AND (CAST(@From AS date) IS NULL OR i.invoice_date >= @From)
          AND (CAST(@To AS date) IS NULL OR i.invoice_date <= @To)
          AND (CAST(@PurchaseType AS text) IS NULL OR i.purchase_type = @PurchaseType)
          AND (CAST(@PaymentStatus AS text) IS NULL OR i.payment_status = @PaymentStatus)
        """;

    public async Task<PagedResult<PurchaseInvoice>> GetInvoicesAsync(int organizationId, PurchaseDocumentQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<PurchaseInvoice>(connection,
            $"SELECT count(*) FROM public.purchase_invoice i LEFT JOIN public.supplier s ON s.supplier_id = i.supplier_id {InvoiceFilter}",
            $"{InvoiceSelect} {InvoiceFilter} ORDER BY i.invoice_date DESC, i.purchase_invoice_id DESC",
            ListParams(organizationId, query), query.Limit, query.Offset, cancellationToken);
    }

    public async Task<PurchaseInvoice?> GetInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var invoice = await connection.QuerySingleOrDefaultAsync<PurchaseInvoice>(new CommandDefinition(
            $"{InvoiceSelect} WHERE i.organization_id = @Org AND i.purchase_invoice_id = @Id",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
        if (invoice is null)
            return null;

        invoice.Lines = (await connection.QueryAsync<PurchaseLine>(new CommandDefinition($"""
            SELECT l.purchase_invoice_line_id AS Id, l.line_no AS LineNo, l.product_id AS ProductId, {ProductColumns},
                   l.description AS Description, l.unit_id AS UnitId, l.conversion_factor AS ConversionFactor,
                   l.quantity AS Quantity, l.base_quantity AS BaseQuantity, l.unit_price AS UnitPrice,
                   l.discount_percent AS DiscountPercent, l.discount_amount AS DiscountAmount, l.tax_group_id AS TaxGroupId,
                   tg.tax_group_name AS TaxGroupName, l.tax_amount AS TaxAmount, l.line_total AS LineTotal,
                   l.purchase_order_line_id AS PurchaseOrderLineId, l.goods_receipt_line_id AS GoodsReceiptLineId,
                   coalesce(b.batch_no, l.batch_no) AS BatchNo, coalesce(b.manufacture_date, l.manufacture_date) AS ManufactureDate,
                   coalesce(b.expiry_date, l.expiry_date) AS ExpiryDate, l.batch_id AS BatchId, l.charge_amount AS ChargeAmount,
                   l.landed_unit_cost AS LandedUnitCost, l.returned_quantity AS ReturnedQuantity
            FROM public.purchase_invoice_line l
            {ProductJoins}
            LEFT JOIN public.tax_group tg ON tg.tax_group_id = l.tax_group_id
            LEFT JOIN public.batch b ON b.batch_id = l.batch_id
            WHERE l.purchase_invoice_id = @Id
            ORDER BY l.line_no
            """, new { Id = id }, cancellationToken: cancellationToken))).AsList();
        await AttachSerialNosAsync(connection, "public.purchase_invoice_serial", "purchase_invoice_line_id", invoice.Lines, cancellationToken);

        var taxes = (await connection.QueryAsync<long, LineTaxItem, (long, LineTaxItem)>(new CommandDefinition("""
            SELECT t.purchase_invoice_line_id AS LineId, t.tax_rate_id AS TaxRateId, r.tax_code AS TaxCode,
                   t.taxable_amount AS TaxableAmount, t.tax_amount AS TaxAmount, r.is_recoverable AS IsRecoverable
            FROM public.purchase_invoice_line_tax t JOIN public.tax_rate r ON r.tax_rate_id = t.tax_rate_id
            WHERE t.purchase_invoice_line_id = ANY(@Ids)
            """, new { Ids = invoice.Lines.Select(l => l.Id).ToArray() }, cancellationToken: cancellationToken),
            (lineId, tax) => (lineId, tax), splitOn: "TaxRateId")).ToLookup(t => t.Item1, t => t.Item2);
        foreach (var line in invoice.Lines)
            line.Taxes = taxes[line.Id].ToList();

        invoice.Charges = await ChargesAsync(connection, "purchase_invoice_id", id, invoice.SupplierId, cancellationToken);
        invoice.Allocations = (await connection.QueryAsync<InvoiceAllocationItem>(new CommandDefinition("""
            SELECT a.invoice_allocation_id AS Id, a.allocation_date AS AllocationDate, a.amount AS Amount,
                   CASE WHEN a.supplier_payment_id IS NOT NULL THEN 'Payment' ELSE 'Return' END AS SourceType,
                   coalesce(a.supplier_payment_id, a.purchase_return_id) AS SourceId,
                   coalesce(p.payment_no, r.return_no) AS SourceNo
            FROM public.purchase_invoice_allocation a
            LEFT JOIN public.supplier_payment p ON p.supplier_payment_id = a.supplier_payment_id
            LEFT JOIN public.purchase_return r ON r.purchase_return_id = a.purchase_return_id
            WHERE a.purchase_invoice_id = @Id
            ORDER BY a.allocation_date, a.invoice_allocation_id
            """, new { Id = id }, cancellationToken: cancellationToken))).AsList();
        return invoice;
    }

    public async Task<long> SaveInvoiceAsync(int organizationId, PurchaseInvoice invoice, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var parameters = new DynamicParameters(new
        {
            Org = organizationId,
            invoice.InvoiceDate,
            invoice.PurchaseType,
            invoice.DueDate,
            invoice.SupplierId,
            SupplierName = invoice.SupplierId is null ? invoice.SupplierName : null,
            invoice.SupplierMobile,
            invoice.ReceivesStock,
            invoice.WarehouseId,
            invoice.PurchaseOrderId,
            invoice.SupplierInvoiceNo,
            invoice.SupplierInvoiceDate,
            invoice.TaxInvoiceNo,
            invoice.CurrencyLookupDataId,
            invoice.ExchangeRate,
            invoice.SubtotalAmount,
            invoice.DiscountAmount,
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
            parameters.Add("Number", await DocumentNumbers.NextAsync(connection, transaction, organizationId, DocumentType.PurchaseInvoice, cancellationToken));
            id = await connection.QuerySingleAsync<long>(new CommandDefinition($"""
                INSERT INTO public.purchase_invoice (
                    organization_id, invoice_no, invoice_date, purchase_type, due_date, supplier_id, supplier_name,
                    supplier_mobile, receives_stock, warehouse_id, purchase_order_id, supplier_invoice_no,
                    supplier_invoice_date, tax_invoice_no, currency_lookup_data_id, exchange_rate, subtotal_amount,
                    discount_amount, tax_amount, charge_amount, round_off_amount, total_amount, paid_amount,
                    payment_method, payment_provider, payment_reference_no, settled_amount, status, payment_status,
                    remarks, created_at, created_by)
                VALUES (
                    @Org, @Number, @InvoiceDate, @PurchaseType, @DueDate, @SupplierId, @SupplierName,
                    @SupplierMobile, @ReceivesStock, @WarehouseId, @PurchaseOrderId, @SupplierInvoiceNo,
                    @SupplierInvoiceDate, @TaxInvoiceNo, @CurrencyLookupDataId, @ExchangeRate, @SubtotalAmount,
                    @DiscountAmount, @TaxAmount, @ChargeAmount, @RoundOffAmount, @TotalAmount, @PaidAmount,
                    @PaymentMethod, @PaymentProvider, @PaymentReferenceNo, @SettledAmount, 'Draft', 'Unpaid',
                    @Remarks, {SecurityDb.NowUtc}, @UserId)
                RETURNING purchase_invoice_id
                """, parameters, transaction, cancellationToken: cancellationToken));
        }
        else
        {
            id = invoice.Id;
            var header = await LockAsync(connection, transaction, "public.purchase_invoice", "purchase_invoice_id", "invoice_no",
                "invoice_date", "warehouse_id", organizationId, id, "invoice", cancellationToken);
            RequireStatus(header, DocumentStatus.Draft, "Only a draft invoice can be changed.");
            parameters.Add("Id", id);
            await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE public.purchase_invoice
                SET invoice_date = @InvoiceDate, purchase_type = @PurchaseType, due_date = @DueDate, supplier_id = @SupplierId,
                    supplier_name = @SupplierName, supplier_mobile = @SupplierMobile, receives_stock = @ReceivesStock,
                    warehouse_id = @WarehouseId, purchase_order_id = @PurchaseOrderId, supplier_invoice_no = @SupplierInvoiceNo,
                    supplier_invoice_date = @SupplierInvoiceDate, tax_invoice_no = @TaxInvoiceNo,
                    currency_lookup_data_id = @CurrencyLookupDataId, exchange_rate = @ExchangeRate,
                    subtotal_amount = @SubtotalAmount, discount_amount = @DiscountAmount, tax_amount = @TaxAmount,
                    charge_amount = @ChargeAmount, round_off_amount = @RoundOffAmount, total_amount = @TotalAmount,
                    paid_amount = @PaidAmount, payment_method = @PaymentMethod, payment_provider = @PaymentProvider,
                    payment_reference_no = @PaymentReferenceNo, settled_amount = @SettledAmount, remarks = @Remarks,
                    updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                WHERE purchase_invoice_id = @Id;
                DELETE FROM public.purchase_invoice_line WHERE purchase_invoice_id = @Id;
                DELETE FROM public.purchase_charge WHERE purchase_invoice_id = @Id;
                """, parameters, transaction, cancellationToken: cancellationToken));
        }

        foreach (var line in invoice.Lines)
        {
            var lineId = await connection.QuerySingleAsync<long>(new CommandDefinition("""
                INSERT INTO public.purchase_invoice_line (
                    purchase_invoice_id, line_no, goods_receipt_line_id, purchase_order_line_id, product_id, description,
                    unit_id, conversion_factor, quantity, unit_price, discount_percent, discount_amount, tax_group_id,
                    tax_amount, line_total, batch_no, manufacture_date, expiry_date, charge_amount, landed_unit_cost)
                VALUES (
                    @DocumentId, @LineNo, @GoodsReceiptLineId, @PurchaseOrderLineId, @ProductId, @Description,
                    @UnitId, @ConversionFactor, @Quantity, @UnitPrice, @DiscountPercent, @DiscountAmount, @TaxGroupId,
                    @TaxAmount, @LineTotal, @BatchNo, @ManufactureDate, @ExpiryDate, @ChargeAmount, @LandedUnitCost)
                RETURNING purchase_invoice_line_id
                """, new
                {
                    DocumentId = id,
                    line.LineNo,
                    line.GoodsReceiptLineId,
                    line.PurchaseOrderLineId,
                    line.ProductId,
                    line.Description,
                    line.UnitId,
                    line.ConversionFactor,
                    line.Quantity,
                    line.UnitPrice,
                    line.DiscountPercent,
                    line.DiscountAmount,
                    line.TaxGroupId,
                    line.TaxAmount,
                    line.LineTotal,
                    line.BatchNo,
                    line.ManufactureDate,
                    line.ExpiryDate,
                    line.ChargeAmount,
                    line.LandedUnitCost
                }, transaction, cancellationToken: cancellationToken));
            await InsertSerialNosAsync(connection, transaction, "public.purchase_invoice_serial", "purchase_invoice_line_id",
                lineId, line.SerialNos, cancellationToken);
            if (line.Taxes.Count > 0)
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO public.purchase_invoice_line_tax (purchase_invoice_line_id, tax_rate_id, taxable_amount, tax_amount)
                    VALUES (@LineId, @TaxRateId, @TaxableAmount, @TaxAmount)
                    """, line.Taxes.Select(t => new { LineId = lineId, t.TaxRateId, t.TaxableAmount, t.TaxAmount }),
                    transaction, cancellationToken: cancellationToken));
        }

        await InsertChargesAsync(connection, transaction, "purchase_invoice_id", id, invoice.Charges, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public Task<DeleteOutcome> DeleteInvoiceAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => DeleteDraftAsync("public.purchase_invoice", "purchase_invoice_id", organizationId, id, cancellationToken);

    private const string InvoicePostingLines = """
        SELECT l.purchase_invoice_line_id AS Id, l.product_id AS ProductId, p.product_type AS ProductType,
               p.tracking_type AS TrackingType, l.base_quantity AS BaseQuantity, l.quantity AS Quantity,
               l.conversion_factor AS ConversionFactor, l.batch_id AS BatchId, l.batch_no AS BatchNo,
               l.manufacture_date AS ManufactureDate, l.expiry_date AS ExpiryDate, l.landed_unit_cost AS LandedUnitCost,
               l.purchase_order_line_id AS PurchaseOrderLineId, l.goods_receipt_line_id AS GoodsReceiptLineId
        FROM public.purchase_invoice_line l JOIN public.product p ON p.product_id = l.product_id
        WHERE l.purchase_invoice_id = @Id ORDER BY l.line_no
        """;

    public async Task PostInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var header = await LockAsync(connection, transaction, "public.purchase_invoice", "purchase_invoice_id", "invoice_no",
            "invoice_date", "warehouse_id", organizationId, id, "invoice", cancellationToken);
        RequireStatus(header, DocumentStatus.Draft, "Only a draft invoice can be posted.");
        var (receivesStock, orderId) = await connection.QuerySingleAsync<(bool, long?)>(new CommandDefinition(
            "SELECT receives_stock, purchase_order_id FROM public.purchase_invoice WHERE purchase_invoice_id = @Id",
            new { Id = id }, transaction, cancellationToken: cancellationToken));
        var lines = (await connection.QueryAsync<ReceivingLine>(new CommandDefinition(InvoicePostingLines, new { Id = id },
            transaction, cancellationToken: cancellationToken))).AsList();
        if (lines.Count == 0)
            throw new BusinessRuleException("Add at least one line before posting.");

        if (receivesStock)
        {
            await ReceiveStockAsync(connection, transaction, organizationId, userId, header, DocumentType.PurchaseInvoice, lines,
                "public.purchase_invoice_serial", "public.purchase_invoice_line", "purchase_invoice_line_id", cancellationToken);
            await AddToOrderLinesAsync(connection, transaction,
                lines.Where(l => l.PurchaseOrderLineId is not null).Select(l => (l.PurchaseOrderLineId!.Value, l.BaseQuantity)),
                received: true, invoiced: true, cancellationToken);
        }
        else
        {
            await BillReceiptLinesAsync(connection, transaction, organizationId, userId, header, lines, 1, cancellationToken);
            await AddToOrderLinesAsync(connection, transaction,
                lines.Where(l => l.PurchaseOrderLineId is not null).Select(l => (l.PurchaseOrderLineId!.Value, l.BaseQuantity)),
                received: false, invoiced: true, cancellationToken);
        }

        await RecomputeOrderStatusAsync(connection, transaction, orderId, cancellationToken);
        await SetStatusAsync(connection, transaction, "public.purchase_invoice", "purchase_invoice_id", id,
            DocumentStatus.Posted, userId, cancellationToken, posted: true);
        await RecomputeSettlementAsync(connection, transaction, [id], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private sealed class ReceiptLineState
    {
        public decimal Quantity { get; set; }
        public decimal Invoiced { get; set; }
        public decimal Factor { get; set; }
        public decimal UnitNet { get; set; }
        public int WarehouseId { get; set; }
        public long? BatchId { get; set; }
        public string Status { get; set; } = string.Empty;
        public int ProductId { get; set; }
        public int? SupplierId { get; set; }
    }

    /// <summary>
    /// Billing goods already received: counts the lines as invoiced on their receipts (sign -1 undoes it)
    /// and, when the bill's cost differs from the receipt's, adjusts the stock value by the difference.
    /// </summary>
    private static async Task BillReceiptLinesAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, int userId, LockedHeader header,
        IReadOnlyList<ReceivingLine> lines, int sign, CancellationToken cancellationToken)
    {
        var settings = await PostingSettingsReader.ReadAsync(connection, transaction, organizationId, cancellationToken);
        var moves = new List<StockMove>();
        foreach (var line in lines)
        {
            if (line.GoodsReceiptLineId is null)
                throw new BusinessRuleException("Every line of a bill for received goods must come from a goods receipt.");

            var receipt = await connection.QuerySingleOrDefaultAsync<ReceiptLineState>(
                new CommandDefinition("""
                    SELECT l.quantity AS Quantity, l.invoiced_quantity AS Invoiced, l.conversion_factor AS Factor,
                           CASE WHEN l.base_quantity > 0
                                THEN (l.quantity * l.unit_price - l.discount_amount) * g.exchange_rate / l.base_quantity
                                ELSE 0 END AS UnitNet,
                           g.warehouse_id AS WarehouseId, l.batch_id AS BatchId, g.status AS Status,
                           l.product_id AS ProductId, g.supplier_id AS SupplierId
                    FROM public.goods_receipt_line l
                    JOIN public.goods_receipt g ON g.goods_receipt_id = l.goods_receipt_id
                    WHERE l.goods_receipt_line_id = @Id AND g.organization_id = @Org
                    FOR UPDATE OF l
                    """, new { Id = line.GoodsReceiptLineId, Org = organizationId }, transaction, cancellationToken: cancellationToken))
                ?? throw new BusinessRuleException("A billed goods receipt line doesn't exist.");
            if (receipt.Status != DocumentStatus.Posted)
                throw new BusinessRuleException("Only goods on a posted goods receipt can be billed.");
            if (receipt.ProductId != line.ProductId || receipt.SupplierId != header.SupplierId)
                throw new BusinessRuleException("A bill line must be for the same product and supplier as its goods receipt line.");

            var invoiced = receipt.Invoiced + sign * line.BaseQuantity / receipt.Factor;
            if (sign > 0 && invoiced > receipt.Quantity + 0.0001m)
                throw new BusinessRuleException("A line bills more than its goods receipt received (or was already billed).");
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE public.goods_receipt_line SET invoiced_quantity = greatest(0, @Invoiced) WHERE goods_receipt_line_id = @Id",
                new { Id = line.GoodsReceiptLineId, Invoiced = invoiced }, transaction, cancellationToken: cancellationToken));

            var difference = Math.Round((line.LandedUnitCost - receipt.UnitNet) * line.BaseQuantity, 4);
            if (sign > 0 && Math.Abs(difference) >= 0.01m && line.ProductType == "Stock")
                moves.Add(new StockMove(receipt.WarehouseId, line.ProductId, receipt.BatchId, 0, "CostAdjustment", line.Id,
                    ValueChange: difference, Remarks: "Bill price differs from the goods receipt"));
        }

        if (moves.Count > 0)
            await new StockLedger(connection, transaction, organizationId, userId, header.DocumentDate, DocumentType.PurchaseInvoice,
                header.Id, header.Number, settings.AllowNegativeStock).ApplyAsync(moves, cancellationToken);
    }

    public async Task CancelInvoiceAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var header = await LockAsync(connection, transaction, "public.purchase_invoice", "purchase_invoice_id", "invoice_no",
            "invoice_date", "warehouse_id", organizationId, id, "invoice", cancellationToken);
        RequireStatus(header, DocumentStatus.Posted, header.Status == DocumentStatus.Draft
            ? "Delete a draft invoice instead of cancelling it."
            : "This invoice is already cancelled.");

        var blockers = await connection.QuerySingleAsync<(int Allocations, int Returns)>(new CommandDefinition("""
            SELECT (SELECT count(*) FROM public.purchase_invoice_allocation WHERE purchase_invoice_id = @Id)::int,
                   (SELECT count(*) FROM public.purchase_return WHERE purchase_invoice_id = @Id AND status = 'Posted')::int
            """, new { Id = id }, transaction, cancellationToken: cancellationToken));
        if (blockers.Allocations > 0)
            throw new BusinessRuleException("Payments or returns settle this invoice. Cancel them first.");
        if (blockers.Returns > 0)
            throw new BusinessRuleException("Goods from this invoice were returned. Cancel the returns first.");

        var (receivesStock, orderId) = await connection.QuerySingleAsync<(bool, long?)>(new CommandDefinition(
            "SELECT receives_stock, purchase_order_id FROM public.purchase_invoice WHERE purchase_invoice_id = @Id",
            new { Id = id }, transaction, cancellationToken: cancellationToken));
        var lines = (await connection.QueryAsync<ReceivingLine>(new CommandDefinition(InvoicePostingLines, new { Id = id },
            transaction, cancellationToken: cancellationToken))).AsList();
        var settings = await PostingSettingsReader.ReadAsync(connection, transaction, organizationId, cancellationToken);

        if (receivesStock)
            await RemoveReceivedSerialsAsync(connection, transaction, organizationId, userId, DocumentType.PurchaseInvoice, id,
                header.WarehouseId ?? 0, cancellationToken);
        else
            await BillReceiptLinesAsync(connection, transaction, organizationId, userId, header, lines, -1, cancellationToken);

        await new StockLedger(connection, transaction, organizationId, userId, DateOnly.FromDateTime(DateTime.UtcNow),
            DocumentType.PurchaseInvoice, id, header.Number, settings.AllowNegativeStock).ReverseAsync(null, cancellationToken);
        await AddToOrderLinesAsync(connection, transaction,
            lines.Where(l => l.PurchaseOrderLineId is not null).Select(l => (l.PurchaseOrderLineId!.Value, -l.BaseQuantity)),
            received: receivesStock, invoiced: true, cancellationToken);
        await RecomputeOrderStatusAsync(connection, transaction, orderId, cancellationToken);
        await SetStatusAsync(connection, transaction, "public.purchase_invoice", "purchase_invoice_id", id,
            DocumentStatus.Cancelled, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OpenPurchaseInvoice>> GetOpenInvoicesAsync(int organizationId, int supplierId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<OpenPurchaseInvoice>(new CommandDefinition("""
            SELECT purchase_invoice_id AS Id, invoice_no AS InvoiceNo, invoice_date AS InvoiceDate, due_date AS DueDate,
                   supplier_invoice_no AS SupplierInvoiceNo, currency_lookup_data_id AS CurrencyLookupDataId,
                   total_amount AS TotalAmount, settled_amount AS SettledAmount, total_amount - settled_amount AS DueAmount
            FROM public.purchase_invoice
            WHERE organization_id = @Org AND supplier_id = @SupplierId AND status = 'Posted' AND payment_status <> 'Paid'
            ORDER BY coalesce(due_date, invoice_date), invoice_date, purchase_invoice_id
            """, new { Org = organizationId, SupplierId = supplierId }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<bool> SupplierInvoiceNoExistsAsync(
        int organizationId, int supplierId, string supplierInvoiceNo, long? excludeId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (SELECT 1 FROM public.purchase_invoice
                           WHERE organization_id = @Org AND supplier_id = @SupplierId AND status <> 'Cancelled'
                             AND lower(supplier_invoice_no) = lower(@No)
                             AND (CAST(@ExcludeId AS bigint) IS NULL OR purchase_invoice_id <> @ExcludeId))
            """, new { Org = organizationId, SupplierId = supplierId, No = supplierInvoiceNo, ExcludeId = excludeId },
            cancellationToken: cancellationToken));
    }
}
