using Dapper;
using ThinkTechErpDomain.Models.Inventory;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Inventory;

public sealed class StockReportQuery
{
    public string? Search { get; set; }
    public int? WarehouseId { get; set; }
    /// <summary>A category and everything below it (resolved by the service).</summary>
    public IReadOnlyCollection<int>? CategoryIds { get; set; }
    public bool LowStockOnly { get; set; }
    public bool IncludeZero { get; set; }
    /// <summary>One row per product and warehouse instead of per product.</summary>
    public bool ByWarehouse { get; set; }
    public int Limit { get; set; } = 50;
    public int Offset { get; set; }
}

public sealed class MovementReportQuery
{
    public string? Search { get; set; }
    public int? WarehouseId { get; set; }
    public int? ProductId { get; set; }
    public string? MovementType { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public int Limit { get; set; } = 50;
    public int Offset { get; set; }
}

public interface IInventoryReportRepository
{
    Task<ReportPage<StockOnHandRow>> GetStockOnHandAsync(int organizationId, StockReportQuery query, CancellationToken cancellationToken = default);
    Task<StockCard?> GetStockCardAsync(int organizationId, int productId, int? warehouseId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockValuationRow>> GetValuationAsync(int organizationId, string groupBy, int? warehouseId, CancellationToken cancellationToken = default);
    Task<ReportPage<StockMovementRow>> GetMovementsAsync(int organizationId, MovementReportQuery query, CancellationToken cancellationToken = default);
    Task<ReportPage<ReorderRow>> GetReorderAsync(int organizationId, StockReportQuery query, CancellationToken cancellationToken = default);
    Task<ReportPage<ExpiryRow>> GetExpiryAsync(int organizationId, int withinDays, int? warehouseId, string? search, int limit, int offset, CancellationToken cancellationToken = default);
}

public sealed class InventoryReportRepository(IDbConnectionFactory connectionFactory) : IInventoryReportRepository
{
    private const string ProductScope = """
        p.organization_id = @Org AND p.product_type = 'Stock'
        AND (CAST(@Pattern AS text) IS NULL OR p.product_code ILIKE @Pattern OR p.product_name ILIKE @Pattern
             OR p.product_name_local ILIKE @Pattern)
        AND (CAST(@CategoryIds AS int[]) IS NULL OR p.category_id = ANY(@CategoryIds))
        """;

    public async Task<ReportPage<StockOnHandRow>> GetStockOnHandAsync(int organizationId, StockReportQuery query, CancellationToken cancellationToken = default)
    {
        var parameters = new
        {
            Org = organizationId,
            Pattern = RepositoryHelpers.Pattern(query.Search),
            query.WarehouseId,
            CategoryIds = query.CategoryIds?.ToArray(),
            query.LowStockOnly,
            query.IncludeZero,
            ByWarehouse = query.ByWarehouse || query.WarehouseId is not null,
            query.Limit,
            query.Offset
        };
        const string rows = $"""
            WITH stock AS (
                SELECT b.product_id, CASE WHEN @ByWarehouse THEN b.warehouse_id END AS warehouse_id,
                       sum(b.quantity) AS quantity, sum(b.reserved_quantity) AS reserved,
                       sum(b.quantity * b.average_cost) AS value
                FROM inventory.stock_balance b
                WHERE b.organization_id = @Org AND (CAST(@WarehouseId AS int) IS NULL OR b.warehouse_id = @WarehouseId)
                GROUP BY 1, 2
            ),
            report AS (
                SELECT p.product_id AS ProductId, p.product_code AS ProductCode, p.product_name AS ProductName,
                       c.category_name AS CategoryName, u.unit_code AS BaseUnitCode, s.warehouse_id AS WarehouseId,
                       w.warehouse_name AS WarehouseName, coalesce(s.quantity, 0) AS Quantity,
                       coalesce(s.reserved, 0) AS ReservedQuantity, coalesce(s.quantity, 0) - coalesce(s.reserved, 0) AS AvailableQuantity,
                       CASE WHEN coalesce(s.quantity, 0) <> 0 THEN round(s.value / s.quantity, 6) ELSE 0 END AS AverageCost,
                       round(coalesce(s.value, 0), 4) AS StockValue, p.reorder_level AS ReorderLevel,
                       (p.reorder_level > 0 AND coalesce(s.quantity, 0) <= p.reorder_level) AS NeedsReorder
                FROM inventory.product p
                JOIN inventory.unit u ON u.unit_id = p.base_unit_id
                LEFT JOIN inventory.category c ON c.category_id = p.category_id
                LEFT JOIN stock s ON s.product_id = p.product_id
                LEFT JOIN inventory.warehouse w ON w.warehouse_id = s.warehouse_id
                WHERE {ProductScope}
                  AND (@IncludeZero OR coalesce(s.quantity, 0) <> 0)
                  AND (NOT @LowStockOnly OR (p.reorder_level > 0 AND coalesce(s.quantity, 0) <= p.reorder_level))
            )
            """;
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            {rows} SELECT count(*), coalesce(sum(Quantity), 0), coalesce(sum(StockValue), 0) FROM report;
            {rows} SELECT * FROM report ORDER BY ProductName, WarehouseName NULLS FIRST LIMIT @Limit OFFSET @Offset;
            """, parameters, cancellationToken: cancellationToken));
        var (count, quantity, value) = await results.ReadSingleAsync<(long, decimal, decimal)>();
        return new ReportPage<StockOnHandRow>
        {
            Items = (await results.ReadAsync<StockOnHandRow>()).AsList(),
            TotalCount = (int)count,
            Limit = query.Limit,
            Offset = query.Offset,
            Totals = new() { ["quantity"] = quantity, ["stockValue"] = value }
        };
    }

    public async Task<StockCard?> GetStockCardAsync(
        int organizationId, int productId, int? warehouseId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        var card = await connection.QuerySingleOrDefaultAsync<StockCard>(new CommandDefinition("""
            SELECT p.product_id AS ProductId, p.product_code AS ProductCode, p.product_name AS ProductName,
                   u.unit_code AS BaseUnitCode, w.warehouse_id AS WarehouseId, w.warehouse_name AS WarehouseName,
                   coalesce((SELECT sum(m.quantity) FROM inventory.stock_movement m
                             WHERE m.product_id = p.product_id AND m.movement_date < @From
                               AND (CAST(@WarehouseId AS int) IS NULL OR m.warehouse_id = @WarehouseId)), 0) AS OpeningQuantity,
                   coalesce((SELECT sum(m.total_cost) FROM inventory.stock_movement m
                             WHERE m.product_id = p.product_id AND m.movement_date < @From
                               AND (CAST(@WarehouseId AS int) IS NULL OR m.warehouse_id = @WarehouseId)), 0) AS OpeningValue
            FROM inventory.product p
            JOIN inventory.unit u ON u.unit_id = p.base_unit_id
            LEFT JOIN inventory.warehouse w ON w.warehouse_id = @WarehouseId AND w.organization_id = p.organization_id
            WHERE p.organization_id = @Org AND p.product_id = @ProductId
            """, new { Org = organizationId, ProductId = productId, WarehouseId = warehouseId, From = from },
            cancellationToken: cancellationToken));
        if (card is null)
            return null;

        var entries = (await connection.QueryAsync<StockCardEntry>(new CommandDefinition("""
            SELECT m.stock_movement_id AS MovementId, m.movement_date AS MovementDate, m.movement_type AS MovementType,
                   m.source_document_type AS DocumentType, m.source_document_id AS DocumentId, m.source_document_no AS DocumentNo,
                   w.warehouse_name AS WarehouseName, b.batch_no AS BatchNo,
                   greatest(m.quantity, 0) AS QuantityIn, greatest(-m.quantity, 0) AS QuantityOut,
                   m.unit_cost AS UnitCost, m.total_cost AS Value
            FROM inventory.stock_movement m
            JOIN inventory.warehouse w ON w.warehouse_id = m.warehouse_id
            LEFT JOIN inventory.batch b ON b.batch_id = m.batch_id
            WHERE m.organization_id = @Org AND m.product_id = @ProductId AND m.movement_date BETWEEN @From AND @To
              AND (CAST(@WarehouseId AS int) IS NULL OR m.warehouse_id = @WarehouseId)
            ORDER BY m.movement_date, m.stock_movement_id
            """, new { Org = organizationId, ProductId = productId, WarehouseId = warehouseId, From = from, To = to },
            cancellationToken: cancellationToken))).AsList();

        var balance = card.OpeningQuantity;
        foreach (var entry in entries)
        {
            balance += entry.QuantityIn - entry.QuantityOut;
            entry.Balance = balance;
        }

        card.From = from;
        card.To = to;
        card.QuantityIn = entries.Sum(e => e.QuantityIn);
        card.QuantityOut = entries.Sum(e => e.QuantityOut);
        card.ClosingQuantity = balance;
        card.ClosingValue = card.OpeningValue + entries.Sum(e => e.Value);
        card.Entries = entries;
        return card;
    }

    public async Task<IReadOnlyList<StockValuationRow>> GetValuationAsync(
        int organizationId, string groupBy, int? warehouseId, CancellationToken cancellationToken = default)
    {
        var (key, name, join) = groupBy switch
        {
            "warehouse" => ("w.warehouse_id", "w.warehouse_name", "JOIN inventory.warehouse w ON w.warehouse_id = b.warehouse_id"),
            "brand" => ("br.brand_id", "coalesce(br.brand_name, 'No brand')", "LEFT JOIN inventory.brand br ON br.brand_id = p.brand_id"),
            _ => ("c.category_id", "coalesce(c.category_name, 'Uncategorized')", "LEFT JOIN inventory.category c ON c.category_id = p.category_id")
        };
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return (await connection.QueryAsync<StockValuationRow>(new CommandDefinition($"""
            SELECT {key} AS GroupId, {name} AS GroupName, count(DISTINCT b.product_id)::int AS ProductCount,
                   sum(b.quantity) AS Quantity, round(sum(b.quantity * b.average_cost), 4) AS StockValue
            FROM inventory.stock_balance b
            JOIN inventory.product p ON p.product_id = b.product_id
            {join}
            WHERE b.organization_id = @Org AND b.quantity <> 0
              AND (CAST(@WarehouseId AS int) IS NULL OR b.warehouse_id = @WarehouseId)
            GROUP BY {key}, {name}
            ORDER BY StockValue DESC
            """, new { Org = organizationId, WarehouseId = warehouseId }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<ReportPage<StockMovementRow>> GetMovementsAsync(int organizationId, MovementReportQuery query, CancellationToken cancellationToken = default)
    {
        const string filter = """
            FROM inventory.stock_movement m
            JOIN inventory.product p ON p.product_id = m.product_id
            JOIN inventory.unit u ON u.unit_id = p.base_unit_id
            JOIN inventory.warehouse w ON w.warehouse_id = m.warehouse_id
            LEFT JOIN inventory.batch b ON b.batch_id = m.batch_id
            WHERE m.organization_id = @Org AND m.movement_date BETWEEN @From AND @To
              AND (CAST(@WarehouseId AS int) IS NULL OR m.warehouse_id = @WarehouseId)
              AND (CAST(@ProductId AS int) IS NULL OR m.product_id = @ProductId)
              AND (CAST(@MovementType AS text) IS NULL OR m.movement_type = @MovementType)
              AND (CAST(@Pattern AS text) IS NULL OR p.product_code ILIKE @Pattern OR p.product_name ILIKE @Pattern
                   OR m.source_document_no ILIKE @Pattern)
            """;
        var parameters = new
        {
            Org = organizationId,
            query.From,
            query.To,
            query.WarehouseId,
            query.ProductId,
            query.MovementType,
            Pattern = RepositoryHelpers.Pattern(query.Search),
            query.Limit,
            query.Offset
        };
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT count(*), coalesce(sum(greatest(m.quantity, 0)), 0), coalesce(sum(greatest(-m.quantity, 0)), 0),
                   coalesce(sum(m.total_cost), 0) {filter};
            SELECT m.stock_movement_id AS Id, m.movement_date AS MovementDate, m.movement_type AS MovementType,
                   w.warehouse_name AS WarehouseName, m.product_id AS ProductId, p.product_code AS ProductCode,
                   p.product_name AS ProductName, u.unit_code AS BaseUnitCode, b.batch_no AS BatchNo, m.quantity AS Quantity,
                   m.unit_cost AS UnitCost, m.total_cost AS TotalCost, m.source_document_type AS DocumentType,
                   m.source_document_id AS DocumentId, m.source_document_no AS DocumentNo,
                   m.reverses_stock_movement_id IS NOT NULL AS IsReversal, m.created_at AT TIME ZONE 'utc' AS CreatedAt
            {filter}
            ORDER BY m.movement_date DESC, m.stock_movement_id DESC
            LIMIT @Limit OFFSET @Offset;
            """, parameters, cancellationToken: cancellationToken));
        var (count, quantityIn, quantityOut, value) = await results.ReadSingleAsync<(long, decimal, decimal, decimal)>();
        return new ReportPage<StockMovementRow>
        {
            Items = (await results.ReadAsync<StockMovementRow>()).AsList(),
            TotalCount = (int)count,
            Limit = query.Limit,
            Offset = query.Offset,
            Totals = new() { ["quantityIn"] = quantityIn, ["quantityOut"] = quantityOut, ["value"] = value }
        };
    }

    public async Task<ReportPage<ReorderRow>> GetReorderAsync(int organizationId, StockReportQuery query, CancellationToken cancellationToken = default)
    {
        var parameters = new
        {
            Org = organizationId,
            Pattern = RepositoryHelpers.Pattern(query.Search),
            query.WarehouseId,
            CategoryIds = query.CategoryIds?.ToArray(),
            query.Limit,
            query.Offset
        };
        const string rows = $"""
            WITH report AS (
                SELECT p.product_id AS ProductId, p.product_code AS ProductCode, p.product_name AS ProductName,
                       c.category_name AS CategoryName, u.unit_code AS BaseUnitCode,
                       coalesce((SELECT sum(b.quantity) FROM inventory.stock_balance b WHERE b.product_id = p.product_id
                                 AND (CAST(@WarehouseId AS int) IS NULL OR b.warehouse_id = @WarehouseId)), 0) AS Quantity,
                       p.reorder_level AS ReorderLevel, p.reorder_quantity AS ReorderQuantity,
                       p.last_purchase_cost AS LastPurchaseCost
                FROM inventory.product p
                JOIN inventory.unit u ON u.unit_id = p.base_unit_id
                LEFT JOIN inventory.category c ON c.category_id = p.category_id
                WHERE {ProductScope} AND p.is_active AND p.reorder_level > 0
            ),
            due AS (
                SELECT *, CASE WHEN ReorderQuantity > 0 THEN ReorderQuantity ELSE greatest(ReorderLevel - Quantity, 0) END AS SuggestedQuantity
                FROM report WHERE Quantity <= ReorderLevel
            )
            """;
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            {rows} SELECT count(*), coalesce(sum(SuggestedQuantity * LastPurchaseCost), 0) FROM due;
            {rows} SELECT * FROM due ORDER BY (Quantity / nullif(ReorderLevel, 0)), ProductName LIMIT @Limit OFFSET @Offset;
            """, parameters, cancellationToken: cancellationToken));
        var (count, value) = await results.ReadSingleAsync<(long, decimal)>();
        return new ReportPage<ReorderRow>
        {
            Items = (await results.ReadAsync<ReorderRow>()).AsList(),
            TotalCount = (int)count,
            Limit = query.Limit,
            Offset = query.Offset,
            Totals = new() { ["estimatedCost"] = Math.Round(value, 4) }
        };
    }

    public async Task<ReportPage<ExpiryRow>> GetExpiryAsync(
        int organizationId, int withinDays, int? warehouseId, string? search, int limit, int offset, CancellationToken cancellationToken = default)
    {
        const string filter = """
            FROM inventory.stock_balance sb
            JOIN inventory.batch b ON b.batch_id = sb.batch_id
            JOIN inventory.product p ON p.product_id = sb.product_id
            JOIN inventory.unit u ON u.unit_id = p.base_unit_id
            JOIN inventory.warehouse w ON w.warehouse_id = sb.warehouse_id
            WHERE sb.organization_id = @Org AND sb.quantity > 0 AND b.expiry_date IS NOT NULL
              AND b.expiry_date <= current_date + @Days
              AND (CAST(@WarehouseId AS int) IS NULL OR sb.warehouse_id = @WarehouseId)
              AND (CAST(@Pattern AS text) IS NULL OR p.product_code ILIKE @Pattern OR p.product_name ILIKE @Pattern
                   OR b.batch_no ILIKE @Pattern)
            """;
        var parameters = new
        {
            Org = organizationId,
            Days = withinDays,
            WarehouseId = warehouseId,
            Pattern = RepositoryHelpers.Pattern(search),
            Limit = limit,
            Offset = offset
        };
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT count(*), coalesce(sum(sb.quantity * sb.average_cost), 0),
                   coalesce(sum(sb.quantity * sb.average_cost) FILTER (WHERE b.expiry_date < current_date), 0) {filter};
            SELECT p.product_id AS ProductId, p.product_code AS ProductCode, p.product_name AS ProductName,
                   u.unit_code AS BaseUnitCode, w.warehouse_id AS WarehouseId, w.warehouse_name AS WarehouseName,
                   b.batch_id AS BatchId, b.batch_no AS BatchNo, b.expiry_date AS ExpiryDate,
                   (b.expiry_date - current_date) AS DaysLeft, sb.quantity AS Quantity,
                   round(sb.quantity * sb.average_cost, 4) AS StockValue
            {filter}
            ORDER BY b.expiry_date, p.product_name
            LIMIT @Limit OFFSET @Offset;
            """, parameters, cancellationToken: cancellationToken));
        var (count, value, expiredValue) = await results.ReadSingleAsync<(long, decimal, decimal)>();
        return new ReportPage<ExpiryRow>
        {
            Items = (await results.ReadAsync<ExpiryRow>()).AsList(),
            TotalCount = (int)count,
            Limit = limit,
            Offset = offset,
            Totals = new() { ["stockValue"] = Math.Round(value, 4), ["expiredValue"] = Math.Round(expiredValue, 4) }
        };
    }
}
