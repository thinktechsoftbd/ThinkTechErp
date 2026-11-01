using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Sales;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Sales;

public sealed partial class SalesDocumentRepository
{
    // =====================================================================
    // Sales returns (always against a posted sale)
    // =====================================================================

    private const string ReturnSelect = """
        SELECT r.sales_return_id AS Id, r.object_id AS ObjectId, r.return_no AS ReturnNo, r.return_date AS ReturnDate,
               r.customer_id AS CustomerId, c.customer_code AS CustomerCode,
               coalesce(c.customer_name, r.customer_name) AS CustomerName, coalesce(r.customer_mobile, c.mobile) AS CustomerMobile,
               r.warehouse_id AS WarehouseId, w.warehouse_name AS WarehouseName,
               r.sales_invoice_id AS SalesInvoiceId, i.invoice_no AS SalesInvoiceNo, r.reason AS Reason,
               r.settlement_type AS SettlementType, r.refund_method AS RefundMethod, r.refund_provider AS RefundProvider,
               r.refund_reference_no AS RefundReferenceNo, r.currency_lookup_data_id AS CurrencyLookupDataId,
               cur.lookup_data_name AS Currency, r.exchange_rate AS ExchangeRate, r.subtotal_amount AS SubtotalAmount,
               r.discount_amount AS DiscountAmount, r.tax_amount AS TaxAmount, r.total_amount AS TotalAmount,
               coalesce((SELECT sum(a.amount) FROM sales.invoice_allocation a WHERE a.sales_return_id = r.sales_return_id), 0) AS AdjustedAmount,
               r.status AS Status,
               (SELECT count(*) FROM sales.sales_return_line l WHERE l.sales_return_id = r.sales_return_id)::int AS LineCount,
               r.posted_at AT TIME ZONE 'utc' AS PostedAt, r.posted_by AS PostedBy, r.remarks AS Remarks,
               r.created_at AT TIME ZONE 'utc' AS CreatedAt, r.created_by AS CreatedBy, r.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM sales.sales_return r
        LEFT JOIN sales.customer c ON c.customer_id = r.customer_id
        JOIN inventory.warehouse w ON w.warehouse_id = r.warehouse_id
        LEFT JOIN sales.sales_invoice i ON i.sales_invoice_id = r.sales_invoice_id
        JOIN public.lookup_data cur ON cur.lookup_data_id = r.currency_lookup_data_id
        """;

    private const string ReturnFilter = """
        WHERE r.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR r.return_no ILIKE @Pattern OR i.invoice_no ILIKE @Pattern
               OR c.customer_name ILIKE @Pattern OR c.mobile ILIKE @Pattern OR r.customer_name ILIKE @Pattern
               OR r.customer_mobile ILIKE @Pattern)
          AND (CAST(@Status AS text) IS NULL OR r.status = @Status)
          AND (CAST(@CustomerId AS int) IS NULL OR r.customer_id = @CustomerId)
          AND (CAST(@From AS date) IS NULL OR r.return_date >= @From)
          AND (CAST(@To AS date) IS NULL OR r.return_date <= @To)
        """;

    public async Task<PagedResult<SalesReturn>> GetReturnsAsync(int organizationId, SalesDocumentQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await RepositoryHelpers.PagedAsync<SalesReturn>(connection, $"""
            SELECT count(*) FROM sales.sales_return r
            LEFT JOIN sales.customer c ON c.customer_id = r.customer_id
            LEFT JOIN sales.sales_invoice i ON i.sales_invoice_id = r.sales_invoice_id
            {ReturnFilter}
            """,
            $"{ReturnSelect} {ReturnFilter} ORDER BY r.return_date DESC, r.sales_return_id DESC",
            ListParams(organizationId, query), query.Limit, query.Offset, cancellationToken);
    }

    public async Task<SalesReturn?> GetReturnAsync(int organizationId, long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        var salesReturn = await connection.QuerySingleOrDefaultAsync<SalesReturn>(new CommandDefinition(
            $"{ReturnSelect} WHERE r.organization_id = @Org AND r.sales_return_id = @Id",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
        if (salesReturn is null)
            return null;

        salesReturn.Lines = (await connection.QueryAsync<SalesReturnLine>(new CommandDefinition("""
            SELECT l.sales_return_line_id AS Id, l.line_no AS LineNo, l.sales_invoice_line_id AS SalesInvoiceLineId,
                   l.product_id AS ProductId, p.product_code AS ProductCode, p.product_name AS ProductName,
                   p.product_type AS ProductType, p.tracking_type AS TrackingType, l.unit_id AS UnitId,
                   u.unit_code AS UnitCode, bu.unit_code AS BaseUnitCode, l.conversion_factor AS ConversionFactor,
                   l.quantity AS Quantity, l.base_quantity AS BaseQuantity, l.batch_id AS BatchId, b.batch_no AS BatchNo,
                   b.expiry_date AS ExpiryDate, l.unit_price AS UnitPrice, l.discount_amount AS DiscountAmount,
                   l.tax_group_id AS TaxGroupId, tg.tax_group_name AS TaxGroupName, l.tax_amount AS TaxAmount,
                   l.line_total AS LineTotal, l.unit_cost AS UnitCost, l.is_restockable AS IsRestockable, l.remarks AS Remarks
            FROM sales.sales_return_line l
            JOIN inventory.product p ON p.product_id = l.product_id
            JOIN inventory.unit u ON u.unit_id = l.unit_id
            JOIN inventory.unit bu ON bu.unit_id = p.base_unit_id
            LEFT JOIN public.tax_group tg ON tg.tax_group_id = l.tax_group_id
            LEFT JOIN inventory.batch b ON b.batch_id = l.batch_id
            WHERE l.sales_return_id = @Id
            ORDER BY l.line_no
            """, new { Id = id }, cancellationToken: cancellationToken))).AsList();

        var serials = (await connection.QueryAsync<(long LineId, string SerialNo)>(new CommandDefinition("""
            SELECT t.sales_return_line_id, s.serial_no
            FROM sales.sales_return_serial t JOIN inventory.serial_number s ON s.serial_number_id = t.serial_number_id
            WHERE t.sales_return_line_id = ANY(@Ids) ORDER BY s.serial_no
            """, new { Ids = salesReturn.Lines.Select(l => l.Id).ToArray() }, cancellationToken: cancellationToken)))
            .ToLookup(s => s.LineId, s => s.SerialNo);
        foreach (var line in salesReturn.Lines)
            line.SerialNos = serials[line.Id].ToList();
        return salesReturn;
    }

    private sealed class ReturnHeader
    {
        public long Id { get; set; }
        public string Number { get; set; } = string.Empty;
        public DateOnly DocumentDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public int WarehouseId { get; set; }
        public long? SalesInvoiceId { get; set; }
        public string SettlementType { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public long CurrencyLookupDataId { get; set; }
    }

    private static async Task<ReturnHeader> LockReturnAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, long id, CancellationToken cancellationToken)
        => await connection.QuerySingleOrDefaultAsync<ReturnHeader>(new CommandDefinition("""
            SELECT sales_return_id AS Id, return_no AS Number, return_date AS DocumentDate, status AS Status,
                   warehouse_id AS WarehouseId, sales_invoice_id AS SalesInvoiceId, settlement_type AS SettlementType,
                   total_amount AS TotalAmount, currency_lookup_data_id AS CurrencyLookupDataId
            FROM sales.sales_return WHERE organization_id = @Org AND sales_return_id = @Id
            FOR UPDATE
            """, new { Org = organizationId, Id = id }, transaction, cancellationToken: cancellationToken))
           ?? throw new BusinessRuleException("The sales return doesn't exist.");

    public async Task<long> SaveReturnAsync(int organizationId, SalesReturn salesReturn, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var parameters = new DynamicParameters(new
        {
            Org = organizationId,
            salesReturn.ReturnDate,
            salesReturn.CustomerId,
            CustomerName = salesReturn.CustomerId is null ? salesReturn.CustomerName : null,
            salesReturn.CustomerMobile,
            salesReturn.WarehouseId,
            salesReturn.SalesInvoiceId,
            salesReturn.Reason,
            salesReturn.SettlementType,
            salesReturn.RefundMethod,
            salesReturn.RefundProvider,
            salesReturn.RefundReferenceNo,
            salesReturn.CurrencyLookupDataId,
            salesReturn.ExchangeRate,
            salesReturn.SubtotalAmount,
            salesReturn.DiscountAmount,
            salesReturn.TaxAmount,
            salesReturn.TotalAmount,
            salesReturn.Remarks,
            UserId = userId
        });

        long id;
        if (salesReturn.Id == 0)
        {
            parameters.Add("Number", await DocumentNumbers.NextAsync(connection, transaction, organizationId, DocumentType.SalesReturn, cancellationToken));
            id = await connection.QuerySingleAsync<long>(new CommandDefinition($"""
                INSERT INTO sales.sales_return (
                    organization_id, return_no, return_date, customer_id, customer_name, customer_mobile, warehouse_id,
                    sales_invoice_id, reason, settlement_type, refund_method, refund_provider, refund_reference_no,
                    currency_lookup_data_id, exchange_rate, subtotal_amount, discount_amount, tax_amount, total_amount,
                    status, remarks, created_at, created_by)
                VALUES (
                    @Org, @Number, @ReturnDate, @CustomerId, @CustomerName, @CustomerMobile, @WarehouseId,
                    @SalesInvoiceId, @Reason, @SettlementType, @RefundMethod, @RefundProvider, @RefundReferenceNo,
                    @CurrencyLookupDataId, @ExchangeRate, @SubtotalAmount, @DiscountAmount, @TaxAmount, @TotalAmount,
                    'Draft', @Remarks, {SecurityDb.NowUtc}, @UserId)
                RETURNING sales_return_id
                """, parameters, transaction, cancellationToken: cancellationToken));
        }
        else
        {
            id = salesReturn.Id;
            var header = await LockReturnAsync(connection, transaction, organizationId, id, cancellationToken);
            if (header.Status != DocumentStatus.Draft)
                throw new BusinessRuleException("Only a draft sales return can be changed.");
            parameters.Add("Id", id);
            await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE sales.sales_return
                SET return_date = @ReturnDate, customer_id = @CustomerId, customer_name = @CustomerName,
                    customer_mobile = @CustomerMobile, warehouse_id = @WarehouseId, sales_invoice_id = @SalesInvoiceId,
                    reason = @Reason, settlement_type = @SettlementType, refund_method = @RefundMethod,
                    refund_provider = @RefundProvider, refund_reference_no = @RefundReferenceNo,
                    currency_lookup_data_id = @CurrencyLookupDataId, exchange_rate = @ExchangeRate,
                    subtotal_amount = @SubtotalAmount, discount_amount = @DiscountAmount, tax_amount = @TaxAmount,
                    total_amount = @TotalAmount, remarks = @Remarks, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                WHERE sales_return_id = @Id;
                DELETE FROM sales.sales_return_line WHERE sales_return_id = @Id;
                """, parameters, transaction, cancellationToken: cancellationToken));
        }

        foreach (var line in salesReturn.Lines)
        {
            var lineId = await connection.QuerySingleAsync<long>(new CommandDefinition("""
                INSERT INTO sales.sales_return_line (
                    sales_return_id, line_no, sales_invoice_line_id, product_id, unit_id, conversion_factor, quantity,
                    batch_id, unit_price, discount_amount, tax_group_id, tax_amount, line_total, unit_cost,
                    is_restockable, remarks)
                VALUES (
                    @DocumentId, @LineNo, @SalesInvoiceLineId, @ProductId, @UnitId, @ConversionFactor, @Quantity,
                    @BatchId, @UnitPrice, @DiscountAmount, @TaxGroupId, @TaxAmount, @LineTotal, @UnitCost,
                    @IsRestockable, @Remarks)
                RETURNING sales_return_line_id
                """, new
                {
                    DocumentId = id,
                    line.LineNo,
                    line.SalesInvoiceLineId,
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
                    line.UnitCost,
                    line.IsRestockable,
                    line.Remarks
                }, transaction, cancellationToken: cancellationToken));

            if (line.SerialNos.Count > 0)
            {
                // Only items sold on that very invoice line can come back on it.
                var added = await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO sales.sales_return_serial (sales_return_line_id, serial_number_id)
                    SELECT @LineId, s.serial_number_id
                    FROM sales.sales_invoice_serial t
                    JOIN inventory.serial_number s ON s.serial_number_id = t.serial_number_id
                    WHERE t.sales_invoice_line_id = @InvoiceLineId AND s.serial_no = ANY(@SerialNos)
                    """, new { LineId = lineId, InvoiceLineId = line.SalesInvoiceLineId, SerialNos = line.SerialNos.ToArray() },
                    transaction, cancellationToken: cancellationToken));
                if (added != line.SerialNos.Count)
                    throw new BusinessRuleException($"Line {line.LineNo}: a serial number wasn't sold on that invoice line.");
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<DeleteOutcome> DeleteReturnAsync(int organizationId, long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        var status = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT status FROM sales.sales_return WHERE organization_id = @Org AND sales_return_id = @Id",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
        if (status is null)
            return DeleteOutcome.NotFound;
        if (status != DocumentStatus.Draft)
            return DeleteOutcome.InUse;
        return await RepositoryHelpers.DeleteAsync(connection, new CommandDefinition(
            "DELETE FROM sales.sales_return WHERE organization_id = @Org AND sales_return_id = @Id AND status = 'Draft'",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
    }

    private const string RefundRoomSql = """
        SELECT i.paid_amount
               + coalesce((SELECT sum(a.amount) FROM sales.invoice_allocation a
                           WHERE a.sales_invoice_id = i.sales_invoice_id AND a.customer_receipt_id IS NOT NULL), 0)
               - coalesce((SELECT sum(r.total_amount) FROM sales.sales_return r
                           WHERE r.sales_invoice_id = i.sales_invoice_id AND r.status = 'Posted'
                             AND r.settlement_type = 'Refund'
                             AND (CAST(@ExcludeId AS bigint) IS NULL OR r.sales_return_id <> @ExcludeId)), 0)
        FROM sales.sales_invoice i
        WHERE i.organization_id = @Org AND i.sales_invoice_id = @InvoiceId
        """;

    public async Task<decimal> GetRefundRoomAsync(int organizationId, long invoiceId, long? excludeReturnId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleOrDefaultAsync<decimal?>(new CommandDefinition(RefundRoomSql,
            new { Org = organizationId, InvoiceId = invoiceId, ExcludeId = excludeReturnId }, cancellationToken: cancellationToken)) ?? 0;
    }

    private sealed class ReturningLine
    {
        public long Id { get; set; }
        public int LineNo { get; set; }
        public int ProductId { get; set; }
        public string ProductType { get; set; } = "Stock";
        public string TrackingType { get; set; } = "None";
        public decimal BaseQuantity { get; set; }
        public decimal Quantity { get; set; }
        public decimal ConversionFactor { get; set; }
        public long? BatchId { get; set; }
        public decimal UnitCost { get; set; }
        public bool IsRestockable { get; set; }
        public long? SalesInvoiceLineId { get; set; }
    }

    private const string ReturnPostingLines = """
        SELECT l.sales_return_line_id AS Id, l.line_no AS LineNo, l.product_id AS ProductId, p.product_type AS ProductType,
               p.tracking_type AS TrackingType, l.base_quantity AS BaseQuantity, l.quantity AS Quantity,
               l.conversion_factor AS ConversionFactor, l.batch_id AS BatchId, l.unit_cost AS UnitCost,
               l.is_restockable AS IsRestockable, l.sales_invoice_line_id AS SalesInvoiceLineId
        FROM sales.sales_return_line l JOIN inventory.product p ON p.product_id = l.product_id
        WHERE l.sales_return_id = @Id ORDER BY l.line_no
        """;

    /// <summary>Adds units returned to the invoice lines they came from (sign -1 undoes it).</summary>
    private static async Task AddReturnedAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IEnumerable<ReturningLine> lines, int sign, CancellationToken cancellationToken)
    {
        foreach (var line in lines.Where(l => l.SalesInvoiceLineId is not null))
        {
            var over = await connection.QuerySingleAsync<bool>(new CommandDefinition("""
                UPDATE sales.sales_invoice_line
                SET returned_quantity = greatest(0, returned_quantity + @Base / conversion_factor)
                WHERE sales_invoice_line_id = @Id
                RETURNING returned_quantity > quantity + 0.0001
                """, new { Id = line.SalesInvoiceLineId, Base = sign * line.BaseQuantity }, transaction, cancellationToken: cancellationToken));
            if (sign > 0 && over)
                throw new BusinessRuleException($"Line {line.LineNo} returns more than was sold on the invoice (or was already returned).");
        }
    }

    public async Task PostReturnAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var header = await LockReturnAsync(connection, transaction, organizationId, id, cancellationToken);
        if (header.Status != DocumentStatus.Draft)
            throw new BusinessRuleException("Only a draft sales return can be posted.");
        var invoiceId = header.SalesInvoiceId ?? throw new BusinessRuleException("A sales return must come from a sale.");

        // The sale is locked too, so two returns of it can't both pass the checks below.
        var invoice = await connection.QuerySingleOrDefaultAsync<(string Status, decimal Due)>(new CommandDefinition("""
            SELECT status, total_amount - settled_amount FROM sales.sales_invoice
            WHERE organization_id = @Org AND sales_invoice_id = @Id
            FOR UPDATE
            """, new { Org = organizationId, Id = invoiceId }, transaction, cancellationToken: cancellationToken));
        if (invoice.Status != DocumentStatus.Posted)
            throw new BusinessRuleException("Goods can only be returned from a completed (posted) sale.");

        var lines = (await connection.QueryAsync<ReturningLine>(new CommandDefinition(ReturnPostingLines, new { Id = id },
            transaction, cancellationToken: cancellationToken))).AsList();
        if (lines.Count == 0)
            throw new BusinessRuleException("Add at least one line before posting.");

        // "More than was sold" is the clearest message, so check it first.
        await AddReturnedAsync(connection, transaction, lines, 1, cancellationToken);

        if (header.SettlementType == SalesReturnSettlement.Refund)
        {
            var room = await connection.QuerySingleAsync<decimal>(new CommandDefinition(RefundRoomSql,
                new { Org = organizationId, InvoiceId = invoiceId, ExcludeId = id }, transaction, cancellationToken: cancellationToken));
            if (header.TotalAmount > room + 0.0001m)
                throw new BusinessRuleException(room <= 0
                    ? "Nothing was paid on this sale yet, so nothing can be refunded. Deduct the return from the customer's dues instead."
                    : $"Only {room:0.##} was paid on this sale and not yet refunded. Refund at most that, or deduct the return from the dues.");
        }

        var serials = (await connection.QueryAsync<(long LineId, long SerialId)>(new CommandDefinition(
            "SELECT sales_return_line_id, serial_number_id FROM sales.sales_return_serial WHERE sales_return_line_id = ANY(@Ids)",
            new { Ids = lines.Select(l => l.Id).ToArray() }, transaction, cancellationToken: cancellationToken)))
            .ToLookup(s => s.LineId, s => s.SerialId);

        var moves = new List<StockMove>();
        foreach (var line in lines.Where(l => l.ProductType == "Stock"))
        {
            var serialIds = serials[line.Id].ToList();
            if (line.TrackingType == "Serial")
            {
                if (serialIds.Count != line.BaseQuantity)
                    throw new BusinessRuleException($"Line {line.LineNo}: choose one serial number per item returned.");
                // Resellable items are back in stock; damaged ones are written off.
                var updated = await connection.ExecuteAsync(new CommandDefinition($"""
                    UPDATE inventory.serial_number
                    SET status = @Status, warehouse_id = @WarehouseId, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                    WHERE serial_number_id = ANY(@Ids) AND status = 'Sold'
                    """, new
                    {
                        Ids = serialIds.ToArray(),
                        Status = line.IsRestockable ? "InStock" : "Scrapped",
                        WarehouseId = line.IsRestockable ? header.WarehouseId : (int?)null,
                        UserId = userId
                    }, transaction, cancellationToken: cancellationToken));
                if (updated != serialIds.Count)
                    throw new BusinessRuleException($"Line {line.LineNo}: one of the serial numbers isn't marked as sold any more.");
            }

            // Damaged goods don't come back into stock: their cost stays in the cost of goods sold.
            if (line.IsRestockable)
                moves.Add(new StockMove(header.WarehouseId, line.ProductId, line.BatchId, line.BaseQuantity, "SaleReturn", line.Id,
                    UnitCost: line.UnitCost > 0 ? line.UnitCost : null, SerialNumberIds: serialIds));
        }

        var settings = await PostingSettingsReader.ReadAsync(connection, transaction, organizationId, cancellationToken);
        await new StockLedger(connection, transaction, organizationId, userId, header.DocumentDate, DocumentType.SalesReturn, id,
            header.Number, settings.AllowNegativeStock).ApplyAsync(moves, cancellationToken);

        // Deducted from dues: settle the sale it came from as far as it is still due; the rest is the customer's advance.
        if (header.SettlementType == SalesReturnSettlement.AdjustDue)
        {
            var amount = Math.Min(header.TotalAmount, Math.Max(0, invoice.Due));
            if (amount > 0)
            {
                await connection.ExecuteAsync(new CommandDefinition($"""
                    INSERT INTO sales.invoice_allocation (organization_id, sales_invoice_id, sales_return_id, allocation_date,
                                                          amount, created_at, created_by)
                    VALUES (@Org, @InvoiceId, @ReturnId, @Date, @Amount, {SecurityDb.NowUtc}, @UserId)
                    """, new { Org = organizationId, InvoiceId = invoiceId, ReturnId = id, Date = header.DocumentDate, Amount = amount, UserId = userId },
                    transaction, cancellationToken: cancellationToken));
                await RecomputeSettlementAsync(connection, transaction, [invoiceId], cancellationToken);
            }
        }

        await SetStatusAsync(connection, transaction, "sales.sales_return", "sales_return_id", id, DocumentStatus.Posted,
            userId, cancellationToken, posted: true);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CancelReturnAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var header = await LockReturnAsync(connection, transaction, organizationId, id, cancellationToken);
        if (header.Status != DocumentStatus.Posted)
            throw new BusinessRuleException(header.Status == DocumentStatus.Draft
                ? "Delete a draft sales return instead of cancelling it."
                : "This sales return is already cancelled.");

        var invoices = (await connection.QueryAsync<long>(new CommandDefinition(
            "DELETE FROM sales.invoice_allocation WHERE sales_return_id = @Id RETURNING sales_invoice_id",
            new { Id = id }, transaction, cancellationToken: cancellationToken))).AsList();
        await RecomputeSettlementAsync(connection, transaction, invoices, cancellationToken);

        var lines = (await connection.QueryAsync<ReturningLine>(new CommandDefinition(ReturnPostingLines, new { Id = id },
            transaction, cancellationToken: cancellationToken))).AsList();
        var serials = (await connection.QueryAsync<(long LineId, long SerialId)>(new CommandDefinition(
            "SELECT sales_return_line_id, serial_number_id FROM sales.sales_return_serial WHERE sales_return_line_id = ANY(@Ids)",
            new { Ids = lines.Select(l => l.Id).ToArray() }, transaction, cancellationToken: cancellationToken)))
            .ToLookup(s => s.LineId, s => s.SerialId);
        foreach (var line in lines.Where(l => l.TrackingType == "Serial"))
        {
            var serialIds = serials[line.Id].ToList();
            if (serialIds.Count == 0)
                continue;
            // Back to sold: resellable items must still be in the warehouse they came back to.
            var updated = await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE inventory.serial_number
                SET status = 'Sold', warehouse_id = NULL, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                WHERE serial_number_id = ANY(@Ids)
                  AND (CASE WHEN @Restockable THEN status = 'InStock' AND warehouse_id = @WarehouseId ELSE status = 'Scrapped' END)
                """, new { Ids = serialIds.ToArray(), Restockable = line.IsRestockable, header.WarehouseId, UserId = userId },
                transaction, cancellationToken: cancellationToken));
            if (updated != serialIds.Count)
                throw new BusinessRuleException($"Line {line.LineNo}: some returned serial items have been sold or moved again.");
        }

        var settings = await PostingSettingsReader.ReadAsync(connection, transaction, organizationId, cancellationToken);
        await new StockLedger(connection, transaction, organizationId, userId, DateOnly.FromDateTime(DateTime.UtcNow),
            DocumentType.SalesReturn, id, header.Number, settings.AllowNegativeStock).ReverseAsync(null, cancellationToken);
        await AddReturnedAsync(connection, transaction, lines, -1, cancellationToken);
        await SetStatusAsync(connection, transaction, "sales.sales_return", "sales_return_id", id, DocumentStatus.Cancelled,
            userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
