namespace ThinkTechErpDomain.Models.Inventory;

/// <summary>A page of report rows plus totals over every row (not just the page).</summary>
public sealed class ReportPage<T>
{
    public IReadOnlyList<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Limit { get; set; }
    public int Offset { get; set; }
    public Dictionary<string, decimal> Totals { get; set; } = [];
}

public sealed class StockOnHandRow
{
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public string BaseUnitCode { get; set; } = string.Empty;
    /// <summary>Null when the report adds up all warehouses.</summary>
    public int? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public decimal Quantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal AvailableQuantity { get; set; }
    public decimal AverageCost { get; set; }
    public decimal StockValue { get; set; }
    public decimal ReorderLevel { get; set; }
    public bool NeedsReorder { get; set; }
}

public sealed class StockCard
{
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string BaseUnitCode { get; set; } = string.Empty;
    public int? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public decimal OpeningQuantity { get; set; }
    public decimal OpeningValue { get; set; }
    public decimal QuantityIn { get; set; }
    public decimal QuantityOut { get; set; }
    public decimal ClosingQuantity { get; set; }
    public decimal ClosingValue { get; set; }
    public IReadOnlyList<StockCardEntry> Entries { get; set; } = [];
}

public sealed class StockCardEntry
{
    public long MovementId { get; set; }
    public DateOnly MovementDate { get; set; }
    public string MovementType { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public long DocumentId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public string? BatchNo { get; set; }
    public decimal QuantityIn { get; set; }
    public decimal QuantityOut { get; set; }
    public decimal UnitCost { get; set; }
    public decimal Value { get; set; }
    /// <summary>Running balance over the report's warehouses.</summary>
    public decimal Balance { get; set; }
}

public sealed class StockValuationRow
{
    public int? GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public int ProductCount { get; set; }
    public decimal Quantity { get; set; }
    public decimal StockValue { get; set; }
}

public sealed class StockMovementRow
{
    public long Id { get; set; }
    public DateOnly MovementDate { get; set; }
    public string MovementType { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string BaseUnitCode { get; set; } = string.Empty;
    public string? BatchNo { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public long DocumentId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public bool IsReversal { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class ReorderRow
{
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public string BaseUnitCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal ReorderLevel { get; set; }
    public decimal ReorderQuantity { get; set; }
    /// <summary>Reorder quantity, or enough to get back to the reorder level when none is set.</summary>
    public decimal SuggestedQuantity { get; set; }
    public decimal LastPurchaseCost { get; set; }
}

public sealed class ExpiryRow
{
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string BaseUnitCode { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public long BatchId { get; set; }
    public string BatchNo { get; set; } = string.Empty;
    public DateOnly ExpiryDate { get; set; }
    /// <summary>Negative once expired.</summary>
    public int DaysLeft { get; set; }
    public decimal Quantity { get; set; }
    public decimal StockValue { get; set; }
}
