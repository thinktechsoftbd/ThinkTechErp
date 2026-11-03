using Dapper;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Purchase;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Purchase;

public sealed partial class PurchaseDocumentRepository
{
    // =====================================================================
    // Purchase orders
    // =====================================================================

    private const string OrderSelect = """
        SELECT o.purchase_order_id AS Id, o.object_id AS ObjectId, o.order_no AS OrderNo, o.order_date AS OrderDate,
               o.expected_date AS ExpectedDate, o.supplier_id AS SupplierId, s.supplier_name AS SupplierName,
               o.warehouse_id AS WarehouseId, w.warehouse_name AS WarehouseName, o.supplier_reference AS SupplierReference,
               o.currency_lookup_data_id AS CurrencyLookupDataId, cur.lookup_data_name AS Currency,
               o.exchange_rate AS ExchangeRate, o.payment_terms_days AS PaymentTermsDays, o.subtotal_amount AS SubtotalAmount,
               o.discount_amount AS DiscountAmount, o.tax_amount AS TaxAmount, o.charge_amount AS ChargeAmount,
               o.total_amount AS TotalAmount, o.status AS Status,
               (SELECT count(*) FROM public.purchase_order_line l WHERE l.purchase_order_id = o.purchase_order_id)::int AS LineCount,
               o.approved_at AT TIME ZONE 'utc' AS ApprovedAt, o.approved_by AS ApprovedBy,
               o.terms_and_conditions AS TermsAndConditions, o.remarks AS Remarks,
               o.created_at AT TIME ZONE 'utc' AS CreatedAt, o.created_by AS CreatedBy, o.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM public.purchase_order o
        JOIN public.supplier s ON s.supplier_id = o.supplier_id
        JOIN public.warehouse w ON w.warehouse_id = o.warehouse_id
        JOIN public.lookup_data cur ON cur.lookup_data_id = o.currency_lookup_data_id
        """;

    private const string OrderFilter = """
        WHERE o.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR o.order_no ILIKE @Pattern OR s.supplier_name ILIKE @Pattern
               OR o.supplier_reference ILIKE @Pattern)
          AND (CAST(@Status AS text) IS NULL OR o.status = @Status)
          AND (CAST(@SupplierId AS int) IS NULL OR o.supplier_id = @SupplierId)
          AND (CAST(@From AS date) IS NULL OR o.order_date >= @From)
          AND (CAST(@To AS date) IS NULL OR o.order_date <= @To)
        """;

    public async Task<PagedResult<PurchaseOrder>> GetOrdersAsync(int organizationId, PurchaseDocumentQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<PurchaseOrder>(connection,
            $"SELECT count(*) FROM public.purchase_order o JOIN public.supplier s ON s.supplier_id = o.supplier_id {OrderFilter}",
            $"{OrderSelect} {OrderFilter} ORDER BY o.order_date DESC, o.purchase_order_id DESC",
            ListParams(organizationId, query), query.Limit, query.Offset, cancellationToken);
    }

    public async Task<PurchaseOrder?> GetOrderAsync(int organizationId, long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var order = await connection.QuerySingleOrDefaultAsync<PurchaseOrder>(new CommandDefinition(
            $"{OrderSelect} WHERE o.organization_id = @Org AND o.purchase_order_id = @Id",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
        if (order is null)
            return null;

        order.Lines = (await connection.QueryAsync<PurchaseLine>(new CommandDefinition($"""
            SELECT l.purchase_order_line_id AS Id, l.line_no AS LineNo, l.product_id AS ProductId, {ProductColumns},
                   l.description AS Description, l.unit_id AS UnitId, l.conversion_factor AS ConversionFactor,
                   l.quantity AS Quantity, l.base_quantity AS BaseQuantity, l.unit_price AS UnitPrice,
                   l.discount_percent AS DiscountPercent, l.discount_amount AS DiscountAmount, l.tax_group_id AS TaxGroupId,
                   tg.tax_group_name AS TaxGroupName, l.tax_amount AS TaxAmount, l.line_total AS LineTotal,
                   l.received_quantity AS ReceivedQuantity, l.invoiced_quantity AS InvoicedQuantity, l.remarks AS Remarks
            FROM public.purchase_order_line l
            {ProductJoins}
            LEFT JOIN public.tax_group tg ON tg.tax_group_id = l.tax_group_id
            WHERE l.purchase_order_id = @Id
            ORDER BY l.line_no
            """, new { Id = id }, cancellationToken: cancellationToken))).AsList();
        return order;
    }

    public async Task<long> SaveOrderAsync(int organizationId, PurchaseOrder order, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var parameters = new DynamicParameters(new
        {
            Org = organizationId,
            order.OrderDate,
            order.ExpectedDate,
            order.SupplierId,
            order.WarehouseId,
            order.SupplierReference,
            order.CurrencyLookupDataId,
            order.ExchangeRate,
            order.PaymentTermsDays,
            order.SubtotalAmount,
            order.DiscountAmount,
            order.TaxAmount,
            order.ChargeAmount,
            order.TotalAmount,
            order.TermsAndConditions,
            order.Remarks,
            UserId = userId
        });

        long id;
        if (order.Id == 0)
        {
            parameters.Add("Number", await DocumentNumbers.NextAsync(connection, transaction, organizationId, DocumentType.PurchaseOrder, cancellationToken));
            id = await connection.QuerySingleAsync<long>(new CommandDefinition($"""
                INSERT INTO public.purchase_order (
                    organization_id, order_no, order_date, expected_date, supplier_id, warehouse_id, supplier_reference,
                    currency_lookup_data_id, exchange_rate, payment_terms_days, subtotal_amount, discount_amount, tax_amount,
                    charge_amount, total_amount, status, terms_and_conditions, remarks, created_at, created_by)
                VALUES (
                    @Org, @Number, @OrderDate, @ExpectedDate, @SupplierId, @WarehouseId, @SupplierReference,
                    @CurrencyLookupDataId, @ExchangeRate, @PaymentTermsDays, @SubtotalAmount, @DiscountAmount, @TaxAmount,
                    @ChargeAmount, @TotalAmount, 'Draft', @TermsAndConditions, @Remarks, {SecurityDb.NowUtc}, @UserId)
                RETURNING purchase_order_id
                """, parameters, transaction, cancellationToken: cancellationToken));
        }
        else
        {
            id = order.Id;
            var header = await LockAsync(connection, transaction, "public.purchase_order", "purchase_order_id", "order_no",
                "order_date", "warehouse_id", organizationId, id, "purchase order", cancellationToken);
            RequireStatus(header, DocumentStatus.Draft, "Only a draft purchase order can be changed.");
            parameters.Add("Id", id);
            await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE public.purchase_order
                SET order_date = @OrderDate, expected_date = @ExpectedDate, supplier_id = @SupplierId, warehouse_id = @WarehouseId,
                    supplier_reference = @SupplierReference, currency_lookup_data_id = @CurrencyLookupDataId,
                    exchange_rate = @ExchangeRate, payment_terms_days = @PaymentTermsDays, subtotal_amount = @SubtotalAmount,
                    discount_amount = @DiscountAmount, tax_amount = @TaxAmount, charge_amount = @ChargeAmount,
                    total_amount = @TotalAmount, terms_and_conditions = @TermsAndConditions, remarks = @Remarks,
                    updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                WHERE purchase_order_id = @Id;
                DELETE FROM public.purchase_order_line WHERE purchase_order_id = @Id;
                """, parameters, transaction, cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO public.purchase_order_line (
                purchase_order_id, line_no, product_id, description, unit_id, conversion_factor, quantity, unit_price,
                discount_percent, discount_amount, tax_group_id, tax_amount, line_total, remarks)
            VALUES (@DocumentId, @LineNo, @ProductId, @Description, @UnitId, @ConversionFactor, @Quantity, @UnitPrice,
                    @DiscountPercent, @DiscountAmount, @TaxGroupId, @TaxAmount, @LineTotal, @Remarks)
            """, order.Lines.Select(l => new
            {
                DocumentId = id,
                l.LineNo,
                l.ProductId,
                l.Description,
                l.UnitId,
                l.ConversionFactor,
                l.Quantity,
                l.UnitPrice,
                l.DiscountPercent,
                l.DiscountAmount,
                l.TaxGroupId,
                l.TaxAmount,
                l.LineTotal,
                l.Remarks
            }), transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public Task<DeleteOutcome> DeleteOrderAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => DeleteDraftAsync("public.purchase_order", "purchase_order_id", organizationId, id, cancellationToken);

    public async Task ChangeOrderStatusAsync(int organizationId, long id, string action, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var header = await LockAsync(connection, transaction, "public.purchase_order", "purchase_order_id", "order_no",
            "order_date", "warehouse_id", organizationId, id, "purchase order", cancellationToken);
        var received = await connection.QuerySingleAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM public.purchase_order_line WHERE purchase_order_id = @Id AND (received_quantity > 0 OR invoiced_quantity > 0))",
            new { Id = id }, transaction, cancellationToken: cancellationToken));

        switch (action)
        {
            case "Approve":
                RequireStatus(header, PurchaseOrderStatus.Draft, "Only a draft purchase order can be approved.");
                await connection.ExecuteAsync(new CommandDefinition($"""
                    UPDATE public.purchase_order SET status = 'Approved', approved_at = {SecurityDb.NowUtc}, approved_by = @UserId,
                        updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                    WHERE purchase_order_id = @Id
                    """, new { Id = id, UserId = userId }, transaction, cancellationToken: cancellationToken));
                break;
            case "Close":
                if (header.Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received))
                    throw new BusinessRuleException("Only an approved purchase order can be closed.");
                await SetStatusAsync(connection, transaction, "public.purchase_order", "purchase_order_id", id,
                    PurchaseOrderStatus.Closed, userId, cancellationToken);
                break;
            case "Cancel":
                if (header.Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Approved))
                    throw new BusinessRuleException("Only a draft or approved purchase order can be cancelled.");
                if (received)
                    throw new BusinessRuleException("Goods were already received or billed against this order; close it instead.");
                await SetStatusAsync(connection, transaction, "public.purchase_order", "purchase_order_id", id,
                    PurchaseOrderStatus.Cancelled, userId, cancellationToken);
                break;
            default:
                throw new BusinessRuleException("Unknown purchase order action.");
        }
        await transaction.CommitAsync(cancellationToken);
    }

    // =====================================================================
    // Goods receipts
    // =====================================================================

    private const string ReceiptSelect = """
        SELECT g.goods_receipt_id AS Id, g.object_id AS ObjectId, g.receipt_no AS ReceiptNo, g.receipt_date AS ReceiptDate,
               g.supplier_id AS SupplierId, s.supplier_name AS SupplierName, g.warehouse_id AS WarehouseId,
               w.warehouse_name AS WarehouseName, g.purchase_order_id AS PurchaseOrderId, po.order_no AS PurchaseOrderNo,
               g.supplier_challan_no AS SupplierChallanNo, g.supplier_challan_date AS SupplierChallanDate,
               g.currency_lookup_data_id AS CurrencyLookupDataId, cur.lookup_data_name AS Currency, g.exchange_rate AS ExchangeRate,
               coalesce((SELECT round(sum(l.base_quantity * l.landed_unit_cost), 4) FROM public.goods_receipt_line l
                         WHERE l.goods_receipt_id = g.goods_receipt_id), 0) AS TotalValue,
               g.status AS Status,
               NOT EXISTS (SELECT 1 FROM public.goods_receipt_line l
                           WHERE l.goods_receipt_id = g.goods_receipt_id AND l.invoiced_quantity < l.quantity) AS IsFullyInvoiced,
               (SELECT count(*) FROM public.goods_receipt_line l WHERE l.goods_receipt_id = g.goods_receipt_id)::int AS LineCount,
               g.posted_at AT TIME ZONE 'utc' AS PostedAt, g.posted_by AS PostedBy, g.remarks AS Remarks,
               g.created_at AT TIME ZONE 'utc' AS CreatedAt, g.created_by AS CreatedBy, g.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM public.goods_receipt g
        LEFT JOIN public.supplier s ON s.supplier_id = g.supplier_id
        JOIN public.warehouse w ON w.warehouse_id = g.warehouse_id
        LEFT JOIN public.purchase_order po ON po.purchase_order_id = g.purchase_order_id
        JOIN public.lookup_data cur ON cur.lookup_data_id = g.currency_lookup_data_id
        """;

    private const string ReceiptFilter = """
        WHERE g.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR g.receipt_no ILIKE @Pattern OR s.supplier_name ILIKE @Pattern
               OR g.supplier_challan_no ILIKE @Pattern)
          AND (CAST(@Status AS text) IS NULL OR g.status = @Status)
          AND (CAST(@SupplierId AS int) IS NULL OR g.supplier_id = @SupplierId)
          AND (CAST(@From AS date) IS NULL OR g.receipt_date >= @From)
          AND (CAST(@To AS date) IS NULL OR g.receipt_date <= @To)
        """;

    public async Task<PagedResult<GoodsReceipt>> GetReceiptsAsync(int organizationId, PurchaseDocumentQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<GoodsReceipt>(connection,
            $"SELECT count(*) FROM public.goods_receipt g LEFT JOIN public.supplier s ON s.supplier_id = g.supplier_id {ReceiptFilter}",
            $"{ReceiptSelect} {ReceiptFilter} ORDER BY g.receipt_date DESC, g.goods_receipt_id DESC",
            ListParams(organizationId, query), query.Limit, query.Offset, cancellationToken);
    }

    public async Task<GoodsReceipt?> GetReceiptAsync(int organizationId, long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var receipt = await connection.QuerySingleOrDefaultAsync<GoodsReceipt>(new CommandDefinition(
            $"{ReceiptSelect} WHERE g.organization_id = @Org AND g.goods_receipt_id = @Id",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
        if (receipt is null)
            return null;

        receipt.Lines = (await connection.QueryAsync<PurchaseLine>(new CommandDefinition($"""
            SELECT l.goods_receipt_line_id AS Id, l.line_no AS LineNo, l.product_id AS ProductId, {ProductColumns},
                   l.unit_id AS UnitId, l.conversion_factor AS ConversionFactor, l.quantity AS Quantity,
                   l.base_quantity AS BaseQuantity, l.rejected_quantity AS RejectedQuantity, l.unit_price AS UnitPrice,
                   l.discount_amount AS DiscountAmount,
                   round(l.quantity * l.unit_price - l.discount_amount, 4) AS LineTotal,
                   l.purchase_order_line_id AS PurchaseOrderLineId,
                   coalesce(b.batch_no, l.batch_no) AS BatchNo, coalesce(b.manufacture_date, l.manufacture_date) AS ManufactureDate,
                   coalesce(b.expiry_date, l.expiry_date) AS ExpiryDate, l.batch_id AS BatchId, l.charge_amount AS ChargeAmount,
                   l.landed_unit_cost AS LandedUnitCost, l.invoiced_quantity AS InvoicedQuantity,
                   l.returned_quantity AS ReturnedQuantity, l.remarks AS Remarks,
                   p.purchase_tax_group_id AS TaxGroupId
            FROM public.goods_receipt_line l
            {ProductJoins}
            LEFT JOIN public.batch b ON b.batch_id = l.batch_id
            WHERE l.goods_receipt_id = @Id
            ORDER BY l.line_no
            """, new { Id = id }, cancellationToken: cancellationToken))).AsList();
        await AttachSerialNosAsync(connection, "public.goods_receipt_serial", "goods_receipt_line_id", receipt.Lines, cancellationToken);
        receipt.Charges = await ChargesAsync(connection, "goods_receipt_id", id, receipt.SupplierId, cancellationToken);
        return receipt;
    }

    public async Task<long> SaveReceiptAsync(int organizationId, GoodsReceipt receipt, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var parameters = new DynamicParameters(new
        {
            Org = organizationId,
            receipt.ReceiptDate,
            receipt.SupplierId,
            receipt.WarehouseId,
            receipt.PurchaseOrderId,
            receipt.SupplierChallanNo,
            receipt.SupplierChallanDate,
            receipt.CurrencyLookupDataId,
            receipt.ExchangeRate,
            receipt.Remarks,
            UserId = userId
        });

        long id;
        if (receipt.Id == 0)
        {
            parameters.Add("Number", await DocumentNumbers.NextAsync(connection, transaction, organizationId, DocumentType.GoodsReceipt, cancellationToken));
            id = await connection.QuerySingleAsync<long>(new CommandDefinition($"""
                INSERT INTO public.goods_receipt (
                    organization_id, receipt_no, receipt_date, supplier_id, warehouse_id, purchase_order_id,
                    supplier_challan_no, supplier_challan_date, currency_lookup_data_id, exchange_rate, status, remarks,
                    created_at, created_by)
                VALUES (@Org, @Number, @ReceiptDate, @SupplierId, @WarehouseId, @PurchaseOrderId, @SupplierChallanNo,
                        @SupplierChallanDate, @CurrencyLookupDataId, @ExchangeRate, 'Draft', @Remarks, {SecurityDb.NowUtc}, @UserId)
                RETURNING goods_receipt_id
                """, parameters, transaction, cancellationToken: cancellationToken));
        }
        else
        {
            id = receipt.Id;
            var header = await LockAsync(connection, transaction, "public.goods_receipt", "goods_receipt_id", "receipt_no",
                "receipt_date", "warehouse_id", organizationId, id, "goods receipt", cancellationToken);
            RequireStatus(header, DocumentStatus.Draft, "Only a draft goods receipt can be changed.");
            parameters.Add("Id", id);
            await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE public.goods_receipt
                SET receipt_date = @ReceiptDate, supplier_id = @SupplierId, warehouse_id = @WarehouseId,
                    purchase_order_id = @PurchaseOrderId, supplier_challan_no = @SupplierChallanNo,
                    supplier_challan_date = @SupplierChallanDate, currency_lookup_data_id = @CurrencyLookupDataId,
                    exchange_rate = @ExchangeRate, remarks = @Remarks, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                WHERE goods_receipt_id = @Id;
                DELETE FROM public.goods_receipt_line WHERE goods_receipt_id = @Id;
                DELETE FROM public.purchase_charge WHERE goods_receipt_id = @Id;
                """, parameters, transaction, cancellationToken: cancellationToken));
        }

        foreach (var line in receipt.Lines)
        {
            var lineId = await connection.QuerySingleAsync<long>(new CommandDefinition("""
                INSERT INTO public.goods_receipt_line (
                    goods_receipt_id, line_no, purchase_order_line_id, product_id, unit_id, conversion_factor, quantity,
                    rejected_quantity, batch_no, manufacture_date, expiry_date, unit_price, discount_amount, charge_amount,
                    landed_unit_cost, remarks)
                VALUES (@DocumentId, @LineNo, @PurchaseOrderLineId, @ProductId, @UnitId, @ConversionFactor, @Quantity,
                        @RejectedQuantity, @BatchNo, @ManufactureDate, @ExpiryDate, @UnitPrice, @DiscountAmount, @ChargeAmount,
                        @LandedUnitCost, @Remarks)
                RETURNING goods_receipt_line_id
                """, new
                {
                    DocumentId = id,
                    line.LineNo,
                    line.PurchaseOrderLineId,
                    line.ProductId,
                    line.UnitId,
                    line.ConversionFactor,
                    line.Quantity,
                    line.RejectedQuantity,
                    line.BatchNo,
                    line.ManufactureDate,
                    line.ExpiryDate,
                    line.UnitPrice,
                    line.DiscountAmount,
                    line.ChargeAmount,
                    line.LandedUnitCost,
                    line.Remarks
                }, transaction, cancellationToken: cancellationToken));
            await InsertSerialNosAsync(connection, transaction, "public.goods_receipt_serial", "goods_receipt_line_id",
                lineId, line.SerialNos, cancellationToken);
        }

        await InsertChargesAsync(connection, transaction, "goods_receipt_id", id, receipt.Charges, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public Task<DeleteOutcome> DeleteReceiptAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => DeleteDraftAsync("public.goods_receipt", "goods_receipt_id", organizationId, id, cancellationToken);

    private const string ReceiptPostingLines = """
        SELECT l.goods_receipt_line_id AS Id, l.product_id AS ProductId, p.product_type AS ProductType,
               p.tracking_type AS TrackingType, l.base_quantity AS BaseQuantity, l.quantity AS Quantity,
               l.conversion_factor AS ConversionFactor, l.batch_id AS BatchId, l.batch_no AS BatchNo,
               l.manufacture_date AS ManufactureDate, l.expiry_date AS ExpiryDate, l.landed_unit_cost AS LandedUnitCost,
               l.purchase_order_line_id AS PurchaseOrderLineId
        FROM public.goods_receipt_line l JOIN public.product p ON p.product_id = l.product_id
        WHERE l.goods_receipt_id = @Id ORDER BY l.line_no
        """;

    public async Task PostReceiptAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var header = await LockAsync(connection, transaction, "public.goods_receipt", "goods_receipt_id", "receipt_no",
            "receipt_date", "warehouse_id", organizationId, id, "goods receipt", cancellationToken);
        RequireStatus(header, DocumentStatus.Draft, "Only a draft goods receipt can be posted.");
        var orderId = await connection.QuerySingleAsync<long?>(new CommandDefinition(
            "SELECT purchase_order_id FROM public.goods_receipt WHERE goods_receipt_id = @Id", new { Id = id }, transaction,
            cancellationToken: cancellationToken));
        var lines = (await connection.QueryAsync<ReceivingLine>(new CommandDefinition(ReceiptPostingLines, new { Id = id },
            transaction, cancellationToken: cancellationToken))).AsList();
        if (lines.Count == 0)
            throw new BusinessRuleException("Add at least one line before posting.");

        await ReceiveStockAsync(connection, transaction, organizationId, userId, header, DocumentType.GoodsReceipt, lines,
            "public.goods_receipt_serial", "public.goods_receipt_line", "goods_receipt_line_id", cancellationToken);
        await AddToOrderLinesAsync(connection, transaction,
            lines.Where(l => l.PurchaseOrderLineId is not null).Select(l => (l.PurchaseOrderLineId!.Value, l.BaseQuantity)),
            received: true, invoiced: false, cancellationToken);
        await RecomputeOrderStatusAsync(connection, transaction, orderId, cancellationToken);
        await SetStatusAsync(connection, transaction, "public.goods_receipt", "goods_receipt_id", id,
            DocumentStatus.Posted, userId, cancellationToken, posted: true);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CancelReceiptAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var header = await LockAsync(connection, transaction, "public.goods_receipt", "goods_receipt_id", "receipt_no",
            "receipt_date", "warehouse_id", organizationId, id, "goods receipt", cancellationToken);
        RequireStatus(header, DocumentStatus.Posted, header.Status == DocumentStatus.Draft
            ? "Delete a draft goods receipt instead of cancelling it."
            : "This goods receipt is already cancelled.");
        var used = await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (SELECT 1 FROM public.goods_receipt_line WHERE goods_receipt_id = @Id
                           AND (invoiced_quantity > 0 OR returned_quantity > 0))
            """, new { Id = id }, transaction, cancellationToken: cancellationToken));
        if (used)
            throw new BusinessRuleException("This receipt was already billed or returned. Cancel those documents first.");

        var orderId = await connection.QuerySingleAsync<long?>(new CommandDefinition(
            "SELECT purchase_order_id FROM public.goods_receipt WHERE goods_receipt_id = @Id", new { Id = id }, transaction,
            cancellationToken: cancellationToken));
        var lines = (await connection.QueryAsync<ReceivingLine>(new CommandDefinition(ReceiptPostingLines, new { Id = id },
            transaction, cancellationToken: cancellationToken))).AsList();
        var settings = await PostingSettingsReader.ReadAsync(connection, transaction, organizationId, cancellationToken);
        await RemoveReceivedSerialsAsync(connection, transaction, organizationId, userId, DocumentType.GoodsReceipt, id,
            header.WarehouseId ?? 0, cancellationToken);
        await new StockLedger(connection, transaction, organizationId, userId, DateOnly.FromDateTime(DateTime.UtcNow),
            DocumentType.GoodsReceipt, id, header.Number, settings.AllowNegativeStock).ReverseAsync(null, cancellationToken);
        await AddToOrderLinesAsync(connection, transaction,
            lines.Where(l => l.PurchaseOrderLineId is not null).Select(l => (l.PurchaseOrderLineId!.Value, -l.BaseQuantity)),
            received: true, invoiced: false, cancellationToken);
        await RecomputeOrderStatusAsync(connection, transaction, orderId, cancellationToken);
        await SetStatusAsync(connection, transaction, "public.goods_receipt", "goods_receipt_id", id,
            DocumentStatus.Cancelled, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
