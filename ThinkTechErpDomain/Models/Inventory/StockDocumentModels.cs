using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.Inventory;

public static class StockAdjustmentReasons
{
    public const string Opening = "Opening";
    public const string CountDifference = "CountDifference";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { Opening, "Damage", "Expired", "Lost", "InternalUse", "Sample", CountDifference, "Other" };
}

/// <summary>One line of a stock document as the user enters it. Quantities are in UnitId.</summary>
public sealed class StockLineRequest
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue)]
    public int UnitId { get; set; }

    /// <summary>Adjustments: + adds stock, - removes it. Transfers: always positive.</summary>
    public decimal Quantity { get; set; }

    /// <summary>An existing batch to take stock from (or add to).</summary>
    [Range(1, long.MaxValue)]
    public long? BatchId { get; set; }

    /// <summary>A new batch for stock added (created when posted).</summary>
    [StringLength(60)]
    public string? BatchNo { get; set; }

    public DateOnly? ManufactureDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }

    /// <summary>Cost per UnitId of stock added (opening stock). Stock removed uses the average cost.</summary>
    [Range(0, 999_999_999_999)]
    public decimal? UnitCost { get; set; }

    /// <summary>Serial-tracked products: one serial number per item.</summary>
    public List<string> SerialNos { get; set; } = [];

    [StringLength(500)]
    public string? Remarks { get; set; }
}

public class StockLine
{
    public long Id { get; set; }
    public int LineNo { get; set; }
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string TrackingType { get; set; } = "None";
    public bool HasExpiry { get; set; }
    public int UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string BaseUnitCode { get; set; } = string.Empty;
    public decimal ConversionFactor { get; set; } = 1;
    public decimal Quantity { get; set; }
    public decimal BaseQuantity { get; set; }
    public long? BatchId { get; set; }
    public string? BatchNo { get; set; }
    public DateOnly? ManufactureDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public decimal? UnitCost { get; set; }
    public string? Remarks { get; set; }
    public List<string> SerialNos { get; set; } = [];
}

// ---- Stock adjustments (and opening stock)

public sealed class StockAdjustment
{
    public long Id { get; set; }
    public string AdjustmentNo { get; set; } = string.Empty;
    public DateOnly AdjustmentDate { get; set; }
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public long? StockCountId { get; set; }
    public string? StockCountNo { get; set; }
    public string Status { get; set; } = "Draft";
    public decimal TotalValue { get; set; }
    public int LineCount { get; set; }
    public DateTime? PostedAt { get; set; }
    public int? PostedBy { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<StockLine> Lines { get; set; } = [];
}

public sealed class StockAdjustmentUpsertRequest
{
    public DateOnly AdjustmentDate { get; set; }

    [Range(1, int.MaxValue)]
    public int WarehouseId { get; set; }

    [Required]
    public string Reason { get; set; } = "Other";

    [StringLength(1000)]
    public string? Remarks { get; set; }

    [MinLength(1, ErrorMessage = "Add at least one line.")]
    public List<StockLineRequest> Lines { get; set; } = [];
}

// ---- Stock transfers

/// <summary>A transfer line; UnitCost is the average cost at dispatch.</summary>
public sealed class StockTransferLine : StockLine
{
    /// <summary>Base units that arrived; null until received.</summary>
    public decimal? ReceivedBaseQuantity { get; set; }
}

public sealed class StockTransfer
{
    public long Id { get; set; }
    public string TransferNo { get; set; } = string.Empty;
    public DateOnly TransferDate { get; set; }
    public int FromWarehouseId { get; set; }
    public string FromWarehouseName { get; set; } = string.Empty;
    public int ToWarehouseId { get; set; }
    public string ToWarehouseName { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public decimal TotalValue { get; set; }
    public int LineCount { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public int? DispatchedBy { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public int? ReceivedBy { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<StockTransferLine> Lines { get; set; } = [];
}

public sealed class StockTransferUpsertRequest
{
    public DateOnly TransferDate { get; set; }

    [Range(1, int.MaxValue)]
    public int FromWarehouseId { get; set; }

    [Range(1, int.MaxValue)]
    public int ToWarehouseId { get; set; }

    [StringLength(1000)]
    public string? Remarks { get; set; }

    [MinLength(1, ErrorMessage = "Add at least one line.")]
    public List<StockLineRequest> Lines { get; set; } = [];
}

public sealed class StockTransferReceiveRequest
{
    /// <summary>What arrived per line, in base units; lines left out arrived in full.</summary>
    public List<StockTransferReceiveLine> Lines { get; set; } = [];
}

public sealed class StockTransferReceiveLine
{
    [Range(1, long.MaxValue)]
    public long LineId { get; set; }

    [Range(0, 999_999_999_999)]
    public decimal ReceivedBaseQuantity { get; set; }
}

// ---- Stock counts

public sealed class StockCount
{
    public long Id { get; set; }
    public string CountNo { get; set; } = string.Empty;
    public DateOnly CountDate { get; set; }
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string Status { get; set; } = "InProgress";
    public int LineCount { get; set; }
    public int CountedLineCount { get; set; }
    public decimal DifferenceValue { get; set; }
    public long? StockAdjustmentId { get; set; }
    public string? StockAdjustmentNo { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? PostedAt { get; set; }
    public int? PostedBy { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public List<StockCountLine> Lines { get; set; } = [];
}

public sealed class StockCountLine
{
    public long Id { get; set; }
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string BaseUnitCode { get; set; } = string.Empty;
    public long? BatchId { get; set; }
    public string? BatchNo { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public decimal SystemQuantity { get; set; }
    public decimal? CountedQuantity { get; set; }
    public decimal? DifferenceQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public string? Remarks { get; set; }
}

public sealed class StockCountCreateRequest
{
    public DateOnly CountDate { get; set; }

    [Range(1, int.MaxValue)]
    public int WarehouseId { get; set; }

    /// <summary>Count one category (and its sub-categories); null = everything in the warehouse.</summary>
    [Range(1, int.MaxValue)]
    public int? CategoryId { get; set; }

    /// <summary>Also list stock products the warehouse has none of (to find stock the system misses).</summary>
    public bool IncludeZeroStock { get; set; }

    [StringLength(1000)]
    public string? Remarks { get; set; }
}

public sealed class StockCountUpdateRequest
{
    [StringLength(1000)]
    public string? Remarks { get; set; }

    public List<StockCountLineUpdate> Lines { get; set; } = [];
}

public sealed class StockCountLineUpdate
{
    [Range(1, long.MaxValue)]
    public long LineId { get; set; }

    /// <summary>Base units counted; null = not counted yet (left out of the adjustment).</summary>
    [Range(0, 999_999_999_999)]
    public decimal? CountedQuantity { get; set; }

    [StringLength(500)]
    public string? Remarks { get; set; }
}
