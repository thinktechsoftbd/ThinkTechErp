using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Purchase;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Purchase;

public sealed partial class PurchaseDocumentRepository
{
    // =====================================================================
    // Purchase returns
    // =====================================================================

    private const string ReturnSelect = """
        SELECT r.purchase_return_id AS Id, r.object_id AS ObjectId, r.return_no AS ReturnNo, r.return_date AS ReturnDate,
               r.supplier_id AS SupplierId, coalesce(s.supplier_name, r.supplier_name) AS SupplierName,
               r.warehouse_id AS WarehouseId, w.warehouse_name AS WarehouseName, r.goods_receipt_id AS GoodsReceiptId,
               g.receipt_no AS GoodsReceiptNo, r.purchase_invoice_id AS PurchaseInvoiceId, i.invoice_no AS PurchaseInvoiceNo,
               r.reason AS Reason, r.settlement_type AS SettlementType, r.refund_method AS RefundMethod,
               r.refund_provider AS RefundProvider, r.refund_reference_no AS RefundReferenceNo,
               r.currency_lookup_data_id AS CurrencyLookupDataId, cur.lookup_data_name AS Currency, r.exchange_rate AS ExchangeRate,
               r.subtotal_amount AS SubtotalAmount, r.discount_amount AS DiscountAmount, r.tax_amount AS TaxAmount,
               r.total_amount AS TotalAmount, r.status AS Status,
               (SELECT count(*) FROM purchase.purchase_return_line l WHERE l.purchase_return_id = r.purchase_return_id)::int AS LineCount,
               r.posted_at AT TIME ZONE 'utc' AS PostedAt, r.posted_by AS PostedBy, r.remarks AS Remarks,
               r.created_at AT TIME ZONE 'utc' AS CreatedAt, r.created_by AS CreatedBy, r.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM purchase.purchase_return r
        LEFT JOIN purchase.supplier s ON s.supplier_id = r.supplier_id
        JOIN inventory.warehouse w ON w.warehouse_id = r.warehouse_id
        LEFT JOIN purchase.goods_receipt g ON g.goods_receipt_id = r.goods_receipt_id
        LEFT JOIN purchase.purchase_invoice i ON i.purchase_invoice_id = r.purchase_invoice_id
        JOIN public.lookup_data cur ON cur.lookup_data_id = r.currency_lookup_data_id
        """;

    private const string ReturnFilter = """
        WHERE r.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR r.return_no ILIKE @Pattern OR s.supplier_name ILIKE @Pattern
               OR r.supplier_name ILIKE @Pattern)
          AND (CAST(@Status AS text) IS NULL OR r.status = @Status)
          AND (CAST(@SupplierId AS int) IS NULL OR r.supplier_id = @SupplierId)
          AND (CAST(@From AS date) IS NULL OR r.return_date >= @From)
          AND (CAST(@To AS date) IS NULL OR r.return_date <= @To)
        """;

    public async Task<PagedResult<PurchaseReturn>> GetReturnsAsync(int organizationId, PurchaseDocumentQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<PurchaseReturn>(connection,
            $"SELECT count(*) FROM purchase.purchase_return r LEFT JOIN purchase.supplier s ON s.supplier_id = r.supplier_id {ReturnFilter}",
            $"{ReturnSelect} {ReturnFilter} ORDER BY r.return_date DESC, r.purchase_return_id DESC",
            ListParams(organizationId, query), query.Limit, query.Offset, cancellationToken);
    }

    public async Task<PurchaseReturn?> GetReturnAsync(int organizationId, long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var purchaseReturn = await connection.QuerySingleOrDefaultAsync<PurchaseReturn>(new CommandDefinition(
            $"{ReturnSelect} WHERE r.organization_id = @Org AND r.purchase_return_id = @Id",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
        if (purchaseReturn is null)
            return null;

        purchaseReturn.Lines = (await connection.QueryAsync<PurchaseLine>(new CommandDefinition($"""
            SELECT l.purchase_return_line_id AS Id, l.line_no AS LineNo, l.product_id AS ProductId, {ProductColumns},
                   l.unit_id AS UnitId, l.conversion_factor AS ConversionFactor, l.quantity AS Quantity,
                   l.base_quantity AS BaseQuantity, l.batch_id AS BatchId, b.batch_no AS BatchNo, b.expiry_date AS ExpiryDate,
                   l.unit_price AS UnitPrice, l.discount_amount AS DiscountAmount, l.tax_group_id AS TaxGroupId,
                   tg.tax_group_name AS TaxGroupName, l.tax_amount AS TaxAmount, l.line_total AS LineTotal,
                   l.goods_receipt_line_id AS GoodsReceiptLineId, l.purchase_invoice_line_id AS PurchaseInvoiceLineId,
                   l.remarks AS Remarks
            FROM purchase.purchase_return_line l
            {ProductJoins}
            LEFT JOIN public.tax_group tg ON tg.tax_group_id = l.tax_group_id
            LEFT JOIN inventory.batch b ON b.batch_id = l.batch_id
            WHERE l.purchase_return_id = @Id
            ORDER BY l.line_no
            """, new { Id = id }, cancellationToken: cancellationToken))).AsList();
        await AttachSerialNosAsync(connection, "purchase.purchase_return_serial", "purchase_return_line_id", purchaseReturn.Lines,
            cancellationToken, viaSerialNumber: true);
        return purchaseReturn;
    }

    public async Task<long> SaveReturnAsync(
        int organizationId, PurchaseReturn purchaseReturn, IReadOnlyDictionary<int, IReadOnlyList<long>> serialIdsByLine, int userId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var parameters = new DynamicParameters(new
        {
            Org = organizationId,
            purchaseReturn.ReturnDate,
            purchaseReturn.SupplierId,
            SupplierName = purchaseReturn.SupplierId is null ? purchaseReturn.SupplierName : null,
            purchaseReturn.WarehouseId,
            purchaseReturn.GoodsReceiptId,
            purchaseReturn.PurchaseInvoiceId,
            purchaseReturn.Reason,
            purchaseReturn.SettlementType,
            purchaseReturn.RefundMethod,
            purchaseReturn.RefundProvider,
            purchaseReturn.RefundReferenceNo,
            purchaseReturn.CurrencyLookupDataId,
            purchaseReturn.ExchangeRate,
            purchaseReturn.SubtotalAmount,
            purchaseReturn.DiscountAmount,
            purchaseReturn.TaxAmount,
            purchaseReturn.TotalAmount,
            purchaseReturn.Remarks,
            UserId = userId
        });

        long id;
        if (purchaseReturn.Id == 0)
        {
            parameters.Add("Number", await DocumentNumbers.NextAsync(connection, transaction, organizationId, DocumentType.PurchaseReturn, cancellationToken));
            id = await connection.QuerySingleAsync<long>(new CommandDefinition($"""
                INSERT INTO purchase.purchase_return (
                    organization_id, return_no, return_date, supplier_id, supplier_name, warehouse_id, goods_receipt_id,
                    purchase_invoice_id, reason, settlement_type, refund_method, refund_provider, refund_reference_no,
                    currency_lookup_data_id, exchange_rate, subtotal_amount, discount_amount, tax_amount, total_amount,
                    status, remarks, created_at, created_by)
                VALUES (
                    @Org, @Number, @ReturnDate, @SupplierId, @SupplierName, @WarehouseId, @GoodsReceiptId,
                    @PurchaseInvoiceId, @Reason, @SettlementType, @RefundMethod, @RefundProvider, @RefundReferenceNo,
                    @CurrencyLookupDataId, @ExchangeRate, @SubtotalAmount, @DiscountAmount, @TaxAmount, @TotalAmount,
                    'Draft', @Remarks, {SecurityDb.NowUtc}, @UserId)
                RETURNING purchase_return_id
                """, parameters, transaction, cancellationToken: cancellationToken));
        }
        else
        {
            id = purchaseReturn.Id;
            var header = await LockAsync(connection, transaction, "purchase.purchase_return", "purchase_return_id", "return_no",
                "return_date", "warehouse_id", organizationId, id, "purchase return", cancellationToken);
            RequireStatus(header, DocumentStatus.Draft, "Only a draft purchase return can be changed.");
            parameters.Add("Id", id);
            await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE purchase.purchase_return
                SET return_date = @ReturnDate, supplier_id = @SupplierId, supplier_name = @SupplierName,
                    warehouse_id = @WarehouseId, goods_receipt_id = @GoodsReceiptId, purchase_invoice_id = @PurchaseInvoiceId,
                    reason = @Reason, settlement_type = @SettlementType, refund_method = @RefundMethod,
                    refund_provider = @RefundProvider, refund_reference_no = @RefundReferenceNo,
                    currency_lookup_data_id = @CurrencyLookupDataId, exchange_rate = @ExchangeRate,
                    subtotal_amount = @SubtotalAmount, discount_amount = @DiscountAmount, tax_amount = @TaxAmount,
                    total_amount = @TotalAmount, remarks = @Remarks, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                WHERE purchase_return_id = @Id;
                DELETE FROM purchase.purchase_return_line WHERE purchase_return_id = @Id;
                """, parameters, transaction, cancellationToken: cancellationToken));
        }

        foreach (var line in purchaseReturn.Lines)
        {
            var lineId = await connection.QuerySingleAsync<long>(new CommandDefinition("""
                INSERT INTO purchase.purchase_return_line (
                    purchase_return_id, line_no, goods_receipt_line_id, purchase_invoice_line_id, product_id, unit_id,
                    conversion_factor, quantity, batch_id, unit_price, discount_amount, tax_group_id, tax_amount, line_total, remarks)
                VALUES (@DocumentId, @LineNo, @GoodsReceiptLineId, @PurchaseInvoiceLineId, @ProductId, @UnitId,
                        @ConversionFactor, @Quantity, @BatchId, @UnitPrice, @DiscountAmount, @TaxGroupId, @TaxAmount, @LineTotal, @Remarks)
                RETURNING purchase_return_line_id
                """, new
                {
                    DocumentId = id,
                    line.LineNo,
                    line.GoodsReceiptLineId,
                    line.PurchaseInvoiceLineId,
                    line.ProductId,
                    line.UnitId,
                    line.ConversionFactor,
                    line.Quantity,
                    line.BatchId,
                    line.UnitPrice,
                    line.DiscountAmount,
                    line.TaxGroupId,
                    line.TaxAmount,
                    line.LineTotal,
                    line.Remarks
                }, transaction, cancellationToken: cancellationToken));
            if (serialIdsByLine.TryGetValue(line.LineNo, out var serialIds) && serialIds.Count > 0)
                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO purchase.purchase_return_serial (purchase_return_line_id, serial_number_id) SELECT @LineId, unnest(@Ids)",
                    new { LineId = lineId, Ids = serialIds.ToArray() }, transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public Task<DeleteOutcome> DeleteReturnAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => DeleteDraftAsync("purchase.purchase_return", "purchase_return_id", organizationId, id, cancellationToken);

    private sealed class ReturnLine
    {
        public long Id { get; set; }
        public int ProductId { get; set; }
        public string ProductType { get; set; } = "Stock";
        public string TrackingType { get; set; } = "None";
        public decimal BaseQuantity { get; set; }
        public long? BatchId { get; set; }
        public long? GoodsReceiptLineId { get; set; }
        public long? PurchaseInvoiceLineId { get; set; }
    }

    private const string ReturnPostingLines = """
        SELECT l.purchase_return_line_id AS Id, l.product_id AS ProductId, p.product_type AS ProductType,
               p.tracking_type AS TrackingType, l.base_quantity AS BaseQuantity, l.batch_id AS BatchId,
               l.goods_receipt_line_id AS GoodsReceiptLineId, l.purchase_invoice_line_id AS PurchaseInvoiceLineId
        FROM purchase.purchase_return_line l JOIN inventory.product p ON p.product_id = l.product_id
        WHERE l.purchase_return_id = @Id ORDER BY l.line_no
        """;

    /// <summary>Adds base units returned to the receipt or invoice lines they came from (negative to undo).</summary>
    private static async Task AddReturnedAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IEnumerable<ReturnLine> lines, int sign, CancellationToken cancellationToken)
    {
        foreach (var line in lines)
        {
            foreach (var (table, key, sourceId) in new[]
                     {
                         ("purchase.goods_receipt_line", "goods_receipt_line_id", line.GoodsReceiptLineId),
                         ("purchase.purchase_invoice_line", "purchase_invoice_line_id", line.PurchaseInvoiceLineId)
                     })
            {
                if (sourceId is null)
                    continue;
                var over = await connection.QuerySingleAsync<bool>(new CommandDefinition($"""
                    UPDATE {table}
                    SET returned_quantity = greatest(0, returned_quantity + @Base / conversion_factor)
                    WHERE {key} = @Id
                    RETURNING returned_quantity > quantity + 0.0001
                    """, new { Id = sourceId, Base = sign * line.BaseQuantity }, transaction, cancellationToken: cancellationToken));
                if (sign > 0 && over)
                    throw new BusinessRuleException("A line returns more than was bought on its invoice or receipt.");
            }
        }
    }

    public async Task PostReturnAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var header = await LockAsync(connection, transaction, "purchase.purchase_return", "purchase_return_id", "return_no",
            "return_date", "warehouse_id", organizationId, id, "purchase return", cancellationToken);
        RequireStatus(header, DocumentStatus.Draft, "Only a draft purchase return can be posted.");
        var warehouseId = header.WarehouseId!.Value;
        var lines = (await connection.QueryAsync<ReturnLine>(new CommandDefinition(ReturnPostingLines, new { Id = id },
            transaction, cancellationToken: cancellationToken))).AsList();
        if (lines.Count == 0)
            throw new BusinessRuleException("Add at least one line before posting.");

        var serials = (await connection.QueryAsync<(long LineId, long SerialId)>(new CommandDefinition(
            "SELECT purchase_return_line_id, serial_number_id FROM purchase.purchase_return_serial WHERE purchase_return_line_id = ANY(@Ids)",
            new { Ids = lines.Select(l => l.Id).ToArray() }, transaction, cancellationToken: cancellationToken)))
            .ToLookup(s => s.LineId, s => s.SerialId);

        var moves = new List<StockMove>();
        foreach (var line in lines.Where(l => l.ProductType == "Stock"))
        {
            if (line.TrackingType == "Batch" && line.BatchId is null)
                throw new BusinessRuleException("Choose the batch for every batch-tracked line.");
            var serialIds = serials[line.Id].ToList();
            if (line.TrackingType == "Serial")
            {
                if (serialIds.Count != line.BaseQuantity)
                    throw new BusinessRuleException("Choose one serial number per item returned.");
                var taken = await connection.ExecuteAsync(new CommandDefinition($"""
                    UPDATE inventory.serial_number SET status = 'ReturnedToSupplier', warehouse_id = NULL,
                        updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                    WHERE serial_number_id = ANY(@Ids) AND status = 'InStock' AND warehouse_id = @WarehouseId
                    """, new { Ids = serialIds.ToArray(), WarehouseId = warehouseId, UserId = userId }, transaction,
                    cancellationToken: cancellationToken));
                if (taken != serialIds.Count)
                    throw new BusinessRuleException("One of the serial numbers isn't in stock in this warehouse.");
            }
            moves.Add(new StockMove(warehouseId, line.ProductId, line.BatchId, -line.BaseQuantity, "PurchaseReturn", line.Id,
                SerialNumberIds: serialIds));
        }

        // "More than was bought" is the clearer message, so check it before the stock.
        await AddReturnedAsync(connection, transaction, lines, 1, cancellationToken);
        var settings = await PostingSettingsReader.ReadAsync(connection, transaction, organizationId, cancellationToken);
        await new StockLedger(connection, transaction, organizationId, userId, header.DocumentDate, DocumentType.PurchaseReturn,
            id, header.Number, settings.AllowNegativeStock).ApplyAsync(moves, cancellationToken);

        // Deducted from what is owed: settle the invoice it came from, as far as that invoice is still due.
        var settlement = await connection.QuerySingleAsync<(string SettlementType, long? InvoiceId, decimal Total, long CurrencyId)>(
            new CommandDefinition("""
                SELECT settlement_type, purchase_invoice_id, total_amount, currency_lookup_data_id
                FROM purchase.purchase_return WHERE purchase_return_id = @Id
                """, new { Id = id }, transaction, cancellationToken: cancellationToken));
        if (settlement is { SettlementType: "AdjustDue", InvoiceId: not null })
        {
            var due = await connection.QuerySingleOrDefaultAsync<decimal?>(new CommandDefinition("""
                SELECT total_amount - settled_amount FROM purchase.purchase_invoice
                WHERE purchase_invoice_id = @InvoiceId AND status = 'Posted' AND currency_lookup_data_id = @CurrencyId
                FOR UPDATE
                """, new { settlement.InvoiceId, settlement.CurrencyId }, transaction, cancellationToken: cancellationToken));
            var amount = Math.Min(settlement.Total, due ?? 0);
            if (amount > 0)
            {
                await connection.ExecuteAsync(new CommandDefinition($"""
                    INSERT INTO purchase.invoice_allocation (organization_id, purchase_invoice_id, purchase_return_id,
                                                             allocation_date, amount, created_at, created_by)
                    VALUES (@Org, @InvoiceId, @ReturnId, @Date, @Amount, {SecurityDb.NowUtc}, @UserId)
                    """, new { Org = organizationId, settlement.InvoiceId, ReturnId = id, Date = header.DocumentDate, Amount = amount, UserId = userId },
                    transaction, cancellationToken: cancellationToken));
                await RecomputeSettlementAsync(connection, transaction, [settlement.InvoiceId.Value], cancellationToken);
            }
        }

        await SetStatusAsync(connection, transaction, "purchase.purchase_return", "purchase_return_id", id,
            DocumentStatus.Posted, userId, cancellationToken, posted: true);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CancelReturnAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var header = await LockAsync(connection, transaction, "purchase.purchase_return", "purchase_return_id", "return_no",
            "return_date", "warehouse_id", organizationId, id, "purchase return", cancellationToken);
        RequireStatus(header, DocumentStatus.Posted, header.Status == DocumentStatus.Draft
            ? "Delete a draft purchase return instead of cancelling it."
            : "This purchase return is already cancelled.");

        var invoices = (await connection.QueryAsync<long>(new CommandDefinition(
            "DELETE FROM purchase.invoice_allocation WHERE purchase_return_id = @Id RETURNING purchase_invoice_id",
            new { Id = id }, transaction, cancellationToken: cancellationToken))).AsList();
        await RecomputeSettlementAsync(connection, transaction, invoices, cancellationToken);

        var serialIds = await StockItems.MovedSerialsAsync(connection, transaction, organizationId, DocumentType.PurchaseReturn, id, cancellationToken);
        await StockItems.RestoreSerialsAsync(connection, transaction, userId, serialIds, header.WarehouseId!.Value, cancellationToken);
        var settings = await PostingSettingsReader.ReadAsync(connection, transaction, organizationId, cancellationToken);
        await new StockLedger(connection, transaction, organizationId, userId, DateOnly.FromDateTime(DateTime.UtcNow),
            DocumentType.PurchaseReturn, id, header.Number, settings.AllowNegativeStock).ReverseAsync(null, cancellationToken);

        var lines = (await connection.QueryAsync<ReturnLine>(new CommandDefinition(ReturnPostingLines, new { Id = id },
            transaction, cancellationToken: cancellationToken))).AsList();
        await AddReturnedAsync(connection, transaction, lines, -1, cancellationToken);
        await SetStatusAsync(connection, transaction, "purchase.purchase_return", "purchase_return_id", id,
            DocumentStatus.Cancelled, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, long>> FindSerialsInStockAsync(
        int organizationId, int productId, int warehouseId, IReadOnlyCollection<string> serialNos, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<(string SerialNo, long Id)>(new CommandDefinition("""
            SELECT serial_no, serial_number_id FROM inventory.serial_number
            WHERE organization_id = @Org AND product_id = @ProductId AND warehouse_id = @WarehouseId AND status = 'InStock'
              AND serial_no = ANY(@SerialNos)
            """, new { Org = organizationId, ProductId = productId, WarehouseId = warehouseId, SerialNos = serialNos.ToArray() },
            cancellationToken: cancellationToken))).ToDictionary(s => s.SerialNo, s => s.Id, StringComparer.Ordinal);
    }

    // =====================================================================
    // Supplier payments (recorded and posted in one step)
    // =====================================================================

    private const string PaymentSelect = """
        SELECT p.supplier_payment_id AS Id, p.object_id AS ObjectId, p.payment_no AS PaymentNo, p.payment_date AS PaymentDate,
               p.supplier_id AS SupplierId, s.supplier_name AS SupplierName, p.payment_type AS PaymentType,
               p.payment_method AS PaymentMethod, p.payment_provider AS PaymentProvider, p.reference_no AS ReferenceNo,
               p.cheque_date AS ChequeDate, p.cheque_status AS ChequeStatus, p.currency_lookup_data_id AS CurrencyLookupDataId,
               cur.lookup_data_name AS Currency, p.exchange_rate AS ExchangeRate, p.amount AS Amount,
               p.tax_deducted_amount AS TaxDeductedAmount, p.allocated_amount AS AllocatedAmount,
               p.amount + p.tax_deducted_amount - p.allocated_amount AS UnallocatedAmount, p.status AS Status,
               p.posted_at AT TIME ZONE 'utc' AS PostedAt, p.posted_by AS PostedBy, p.remarks AS Remarks,
               p.created_at AT TIME ZONE 'utc' AS CreatedAt, p.created_by AS CreatedBy, p.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM purchase.supplier_payment p
        JOIN purchase.supplier s ON s.supplier_id = p.supplier_id
        JOIN public.lookup_data cur ON cur.lookup_data_id = p.currency_lookup_data_id
        """;

    private const string PaymentFilter = """
        WHERE p.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR p.payment_no ILIKE @Pattern OR s.supplier_name ILIKE @Pattern
               OR p.reference_no ILIKE @Pattern)
          AND (CAST(@Status AS text) IS NULL OR p.status = @Status)
          AND (CAST(@SupplierId AS int) IS NULL OR p.supplier_id = @SupplierId)
          AND (CAST(@From AS date) IS NULL OR p.payment_date >= @From)
          AND (CAST(@To AS date) IS NULL OR p.payment_date <= @To)
        """;

    public async Task<PagedResult<SupplierPayment>> GetPaymentsAsync(int organizationId, PurchaseDocumentQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<SupplierPayment>(connection,
            $"SELECT count(*) FROM purchase.supplier_payment p JOIN purchase.supplier s ON s.supplier_id = p.supplier_id {PaymentFilter}",
            $"{PaymentSelect} {PaymentFilter} ORDER BY p.payment_date DESC, p.supplier_payment_id DESC",
            ListParams(organizationId, query), query.Limit, query.Offset, cancellationToken);
    }

    public async Task<SupplierPayment?> GetPaymentAsync(int organizationId, long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var payment = await connection.QuerySingleOrDefaultAsync<SupplierPayment>(new CommandDefinition(
            $"{PaymentSelect} WHERE p.organization_id = @Org AND p.supplier_payment_id = @Id",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
        if (payment is null)
            return null;
        payment.Allocations = (await connection.QueryAsync<PaymentAllocationItem>(new CommandDefinition("""
            SELECT a.purchase_invoice_id AS PurchaseInvoiceId, i.invoice_no AS InvoiceNo, i.invoice_date AS InvoiceDate,
                   i.total_amount AS InvoiceTotal, a.amount AS Amount
            FROM purchase.invoice_allocation a JOIN purchase.purchase_invoice i ON i.purchase_invoice_id = a.purchase_invoice_id
            WHERE a.supplier_payment_id = @Id ORDER BY i.invoice_date, i.purchase_invoice_id
            """, new { Id = id }, cancellationToken: cancellationToken))).AsList();
        return payment;
    }

    public async Task<long> CreatePaymentAsync(int organizationId, SupplierPayment payment, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Re-check what each invoice still owes under lock, so two payments can't both settle the same due.
        foreach (var allocation in payment.Allocations)
        {
            var due = await connection.QuerySingleOrDefaultAsync<decimal?>(new CommandDefinition("""
                SELECT total_amount - settled_amount FROM purchase.purchase_invoice
                WHERE organization_id = @Org AND purchase_invoice_id = @Id AND supplier_id = @SupplierId
                  AND status = 'Posted' AND currency_lookup_data_id = @CurrencyId
                FOR UPDATE
                """, new { Org = organizationId, Id = allocation.PurchaseInvoiceId, payment.SupplierId, CurrencyId = payment.CurrencyLookupDataId },
                transaction, cancellationToken: cancellationToken));
            if (due is null)
                throw new BusinessRuleException("A payment can only settle this supplier's posted invoices in the same currency.");
            if (allocation.Amount > due.Value + 0.0001m)
                throw new BusinessRuleException($"Invoice {allocation.InvoiceNo} has only {due.Value:0.##} left to pay.");
        }

        var number = await DocumentNumbers.NextAsync(connection, transaction, organizationId, DocumentType.SupplierPayment, cancellationToken);
        var id = await connection.QuerySingleAsync<long>(new CommandDefinition($"""
            INSERT INTO purchase.supplier_payment (
                organization_id, payment_no, payment_date, supplier_id, payment_type, payment_method, payment_provider,
                reference_no, cheque_date, cheque_status, currency_lookup_data_id, exchange_rate, amount, tax_deducted_amount,
                allocated_amount, status, posted_at, posted_by, remarks, created_at, created_by)
            VALUES (
                @Org, @Number, @PaymentDate, @SupplierId, @PaymentType, @PaymentMethod, @PaymentProvider,
                @ReferenceNo, @ChequeDate, @ChequeStatus, @CurrencyLookupDataId, @ExchangeRate, @Amount, @TaxDeductedAmount,
                @AllocatedAmount, 'Posted', {SecurityDb.NowUtc}, @UserId, @Remarks, {SecurityDb.NowUtc}, @UserId)
            RETURNING supplier_payment_id
            """, new
            {
                Org = organizationId,
                Number = number,
                payment.PaymentDate,
                payment.SupplierId,
                payment.PaymentType,
                payment.PaymentMethod,
                payment.PaymentProvider,
                payment.ReferenceNo,
                payment.ChequeDate,
                payment.ChequeStatus,
                payment.CurrencyLookupDataId,
                payment.ExchangeRate,
                payment.Amount,
                payment.TaxDeductedAmount,
                AllocatedAmount = payment.Allocations.Sum(a => a.Amount),
                payment.Remarks,
                UserId = userId
            }, transaction, cancellationToken: cancellationToken));

        if (payment.Allocations.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition($"""
                INSERT INTO purchase.invoice_allocation (organization_id, purchase_invoice_id, supplier_payment_id, allocation_date,
                                                         amount, created_at, created_by)
                VALUES (@Org, @InvoiceId, @PaymentId, @Date, @Amount, {SecurityDb.NowUtc}, @UserId)
                """, payment.Allocations.Select(a => new
                {
                    Org = organizationId,
                    InvoiceId = a.PurchaseInvoiceId,
                    PaymentId = id,
                    Date = payment.PaymentDate,
                    a.Amount,
                    UserId = userId
                }), transaction, cancellationToken: cancellationToken));
            await RecomputeSettlementAsync(connection, transaction, payment.Allocations.Select(a => a.PurchaseInvoiceId), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    private async Task<(NpgsqlConnection, NpgsqlTransaction, LockedHeader, string Method, string? ChequeStatus)> LockPaymentAsync(
        int organizationId, long id, CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var header = await LockAsync(connection, transaction, "purchase.supplier_payment", "supplier_payment_id", "payment_no",
            "payment_date", "NULL::int", organizationId, id, "payment", cancellationToken);
        var (method, chequeStatus) = await connection.QuerySingleAsync<(string, string?)>(new CommandDefinition(
            "SELECT payment_method, cheque_status FROM purchase.supplier_payment WHERE supplier_payment_id = @Id",
            new { Id = id }, transaction, cancellationToken: cancellationToken));
        return (connection, transaction, header, method, chequeStatus);
    }

    /// <summary>Takes a payment off the invoices it settled.</summary>
    private static async Task UnallocatePaymentAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long id, int userId, CancellationToken cancellationToken)
    {
        var invoices = (await connection.QueryAsync<long>(new CommandDefinition(
            "DELETE FROM purchase.invoice_allocation WHERE supplier_payment_id = @Id RETURNING purchase_invoice_id",
            new { Id = id }, transaction, cancellationToken: cancellationToken))).AsList();
        await RecomputeSettlementAsync(connection, transaction, invoices, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE purchase.supplier_payment SET allocated_amount = 0, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE supplier_payment_id = @Id
            """, new { Id = id, UserId = userId }, transaction, cancellationToken: cancellationToken));
    }

    public async Task CancelPaymentAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        var (connection, transaction, header, _, _) = await LockPaymentAsync(organizationId, id, cancellationToken);
        await using (connection)
        await using (transaction)
        {
            RequireStatus(header, DocumentStatus.Posted, "This payment is already cancelled.");
            await UnallocatePaymentAsync(connection, transaction, id, userId, cancellationToken);
            await SetStatusAsync(connection, transaction, "purchase.supplier_payment", "supplier_payment_id", id,
                DocumentStatus.Cancelled, userId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public async Task SetChequeStatusAsync(int organizationId, long id, string chequeStatus, int userId, CancellationToken cancellationToken = default)
    {
        var (connection, transaction, header, method, current) = await LockPaymentAsync(organizationId, id, cancellationToken);
        await using (connection)
        await using (transaction)
        {
            RequireStatus(header, DocumentStatus.Posted, "Only a posted payment's cheque can change.");
            if (method != PaymentMethod.Cheque)
                throw new BusinessRuleException("This payment wasn't made by cheque.");
            if (current == "Bounced")
                throw new BusinessRuleException("A bounced cheque stays bounced; record a new payment instead.");

            if (chequeStatus == "Bounced")
                await UnallocatePaymentAsync(connection, transaction, id, userId, cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE purchase.supplier_payment SET cheque_status = @Status, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                WHERE supplier_payment_id = @Id
                """, new { Id = id, Status = chequeStatus, UserId = userId }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
    }
}
