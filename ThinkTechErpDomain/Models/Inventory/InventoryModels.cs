using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.Inventory;

public static class InventoryRules
{
    public const string ViewInventoryPermission = "INVENTORY.V";
    public const string ManageInventoryPermission = "INVENTORY.M";

    public static readonly IReadOnlySet<string> UnitTypes = new HashSet<string>(StringComparer.Ordinal)
        { "Count", "Weight", "Volume", "Length", "Area", "Time", "Other" };

    public static readonly IReadOnlySet<string> WarehouseTypes = new HashSet<string>(StringComparer.Ordinal)
        { "Store", "Warehouse" };

    public static readonly IReadOnlySet<string> ProductTypes = new HashSet<string>(StringComparer.Ordinal)
        { "Stock", "NonStock", "Service" };

    public static readonly IReadOnlySet<string> TrackingTypes = new HashSet<string>(StringComparer.Ordinal)
        { "None", "Batch", "Serial" };
}

// ---- Units

public sealed class Unit
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameLocal { get; set; }
    public string UnitType { get; set; } = "Count";
    public string? IsoCode { get; set; }
    public bool AllowDecimal { get; set; }
    public bool IsActive { get; set; }
    public int ProductCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class UnitUpsertRequest
{
    [Required, StringLength(20, MinimumLength = 1)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(100)]
    public string? NameLocal { get; set; }

    [Required]
    public string UnitType { get; set; } = "Count";

    [StringLength(10)]
    public string? IsoCode { get; set; }

    public bool AllowDecimal { get; set; }
    public bool IsActive { get; set; } = true;
}

// ---- Categories

public sealed class Category
{
    public int Id { get; set; }
    public int? ParentCategoryId { get; set; }
    public string? ParentName { get; set; }
    public string? Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameLocal { get; set; }
    /// <summary>"Fertilizer > Urea": the name with its parents.</summary>
    public string Path { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public int ProductCount { get; set; }
    /// <summary>The mapped ledger head: a public.acc_sub_class id (category.acc_sub_class_id), if any.</summary>
    public int? AccSubClassId { get; set; }
    public string? LedgerHeadCode { get; set; }
    public string? LedgerHeadName { get; set; }
    /// <summary>Map class/main class/account class/parent class of the head.</summary>
    public string? LedgerHeadPath { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class CategoryUpsertRequest
{
    [Range(1, int.MaxValue)]
    public int? ParentCategoryId { get; set; }

    [StringLength(30)]
    public string? Code { get; set; }

    [Required, StringLength(150, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(150)]
    public string? NameLocal { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>The ledger head (acc_sub_class id) to map, or null for none.</summary>
    [Range(1, int.MaxValue)]
    public int? AccSubClassId { get; set; }
}

// ---- Brands

public sealed class Brand
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameLocal { get; set; }
    public bool IsActive { get; set; }
    public int ProductCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class BrandUpsertRequest
{
    [Required, StringLength(150, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(150)]
    public string? NameLocal { get; set; }

    public bool IsActive { get; set; } = true;
}

// ---- Warehouses

public sealed class Warehouse
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameLocal { get; set; }
    public string WarehouseType { get; set; } = "Store";
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; }
    /// <summary>The organization's default warehouse (inventory settings).</summary>
    public bool IsDefault { get; set; }
    public decimal StockValue { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class WarehouseUpsertRequest
{
    [Required, StringLength(30, MinimumLength = 1)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(150, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(150)]
    public string? NameLocal { get; set; }

    [Required]
    public string WarehouseType { get; set; } = "Store";

    [StringLength(500)]
    public string? Address { get; set; }

    [StringLength(30)]
    public string? Phone { get; set; }

    public bool IsActive { get; set; } = true;
}

// ---- Inventory settings (one row per organization)

public sealed class InventorySetting
{
    /// <summary>False until the organization saves its settings; the values are then the defaults.</summary>
    public bool IsConfigured { get; set; }
    public long? BaseCurrencyLookupDataId { get; set; }
    public string? BaseCurrency { get; set; }
    public int FiscalYearStartMonth { get; set; } = 7;
    public bool PricesIncludeTax { get; set; }
    public bool AllowNegativeStock { get; set; }
    public bool RequireApproval { get; set; }
    public int ExpiryAlertDays { get; set; } = 30;
    public int? DefaultWarehouseId { get; set; }
    public string? DefaultWarehouseName { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class InventorySettingUpdateRequest
{
    [Range(1, long.MaxValue)]
    public long BaseCurrencyLookupDataId { get; set; }

    [Range(1, 12)]
    public int FiscalYearStartMonth { get; set; } = 7;

    public bool PricesIncludeTax { get; set; }
    public bool AllowNegativeStock { get; set; }
    public bool RequireApproval { get; set; }

    [Range(0, 3650)]
    public int ExpiryAlertDays { get; set; } = 30;

    [Range(1, int.MaxValue)]
    public int? DefaultWarehouseId { get; set; }
}

// ---- Products

public sealed class Product
{
    public int Id { get; set; }
    public Guid ObjectId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameLocal { get; set; }
    public string ProductType { get; set; } = "Stock";
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public int? BrandId { get; set; }
    public string? BrandName { get; set; }
    public int BaseUnitId { get; set; }
    public string BaseUnitCode { get; set; } = string.Empty;
    public bool BaseUnitAllowsDecimal { get; set; }
    public string TrackingType { get; set; } = "None";
    public bool HasExpiry { get; set; }
    public int? ShelfLifeDays { get; set; }
    public string? GenericName { get; set; }
    public string? RegistrationNo { get; set; }
    public string? HsCode { get; set; }
    public string? CountryOfOrigin { get; set; }
    public int? PurchaseTaxGroupId { get; set; }
    public string? PurchaseTaxGroupName { get; set; }
    public int? SaleTaxGroupId { get; set; }
    public decimal LastPurchaseCost { get; set; }
    public decimal SalePrice { get; set; }
    public decimal? Mrp { get; set; }
    public decimal ReorderLevel { get; set; }
    public decimal ReorderQuantity { get; set; }
    public Dictionary<string, string> Attributes { get; set; } = [];
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; }
    /// <summary>Base units on hand in all warehouses.</summary>
    public decimal StockQuantity { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    /// <summary>Every unit the product is bought or sold in, the base unit first (factor 1).</summary>
    public List<ProductUnit> Units { get; set; } = [];
    public List<ProductBarcode> Barcodes { get; set; } = [];
}

public sealed class ProductUnit
{
    public int UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public decimal ConversionFactor { get; set; } = 1;
    public decimal? SalePrice { get; set; }
    public bool IsPurchaseDefault { get; set; }
    public bool IsSaleDefault { get; set; }
}

public sealed class ProductBarcode
{
    public int? UnitId { get; set; }
    public string? UnitCode { get; set; }
    public string Barcode { get; set; } = string.Empty;
}

public sealed class ProductUpsertRequest
{
    /// <summary>Optional: a new product without one is numbered P-00001, P-00002, ...; an edit without one keeps its code.</summary>
    [StringLength(50)]
    public string? Code { get; set; }

    [Required, StringLength(250, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(250)]
    public string? NameLocal { get; set; }

    [Required]
    public string ProductType { get; set; } = "Stock";

    [Range(1, int.MaxValue)]
    public int? CategoryId { get; set; }

    [Range(1, int.MaxValue)]
    public int? BrandId { get; set; }

    [Range(1, int.MaxValue)]
    public int BaseUnitId { get; set; }

    [Required]
    public string TrackingType { get; set; } = "None";

    public bool HasExpiry { get; set; }

    [Range(1, 36500)]
    public int? ShelfLifeDays { get; set; }

    [StringLength(250)]
    public string? GenericName { get; set; }

    [StringLength(100)]
    public string? RegistrationNo { get; set; }

    [StringLength(20)]
    public string? HsCode { get; set; }

    [StringLength(2, MinimumLength = 2)]
    public string? CountryOfOrigin { get; set; }

    [Range(1, int.MaxValue)]
    public int? PurchaseTaxGroupId { get; set; }

    [Range(1, int.MaxValue)]
    public int? SaleTaxGroupId { get; set; }

    [Range(0, 999_999_999_999)]
    public decimal SalePrice { get; set; }

    [Range(0, 999_999_999_999)]
    public decimal? Mrp { get; set; }

    [Range(0, 999_999_999_999)]
    public decimal ReorderLevel { get; set; }

    [Range(0, 999_999_999_999)]
    public decimal ReorderQuantity { get; set; }

    public Dictionary<string, string>? Attributes { get; set; }

    [StringLength(500)]
    public string? ImageUrl { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Packs other than the base unit (the base unit is always kept, factor 1).</summary>
    public List<ProductUnitRequest> Units { get; set; } = [];

    public List<ProductBarcodeRequest> Barcodes { get; set; } = [];

    /// <summary>The base unit's own defaults (when no pack is the default).</summary>
    public bool BaseIsPurchaseDefault { get; set; } = true;
    public bool BaseIsSaleDefault { get; set; } = true;
}

public sealed class ProductUnitRequest
{
    [Range(1, int.MaxValue)]
    public int UnitId { get; set; }

    [Range(typeof(decimal), "0.000001", "999999999999")]
    public decimal ConversionFactor { get; set; }

    [Range(0, 999_999_999_999)]
    public decimal? SalePrice { get; set; }

    public bool IsPurchaseDefault { get; set; }
    public bool IsSaleDefault { get; set; }
}

public sealed class ProductBarcodeRequest
{
    [Range(1, int.MaxValue)]
    public int? UnitId { get; set; }

    [Required, StringLength(64, MinimumLength = 1)]
    public string Barcode { get; set; } = string.Empty;
}

/// <summary>A product as a document line picks it: units, tracking, last cost and stock.</summary>
public sealed class ProductLookupItem
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameLocal { get; set; }
    public string ProductType { get; set; } = "Stock";
    public string TrackingType { get; set; } = "None";
    public bool HasExpiry { get; set; }
    public int BaseUnitId { get; set; }
    public string BaseUnitCode { get; set; } = string.Empty;
    public bool AllowDecimal { get; set; }
    public int? PurchaseTaxGroupId { get; set; }
    public int? SaleTaxGroupId { get; set; }
    public decimal LastPurchaseCost { get; set; }
    public decimal SalePrice { get; set; }
    /// <summary>Maximum retail price per base unit, where the pack prints one.</summary>
    public decimal? Mrp { get; set; }
    public decimal StockQuantity { get; set; }
    /// <summary>Set when the search matched one of the product's barcodes.</summary>
    public string? MatchedBarcode { get; set; }
    public int? MatchedUnitId { get; set; }
    public List<ProductUnit> Units { get; set; } = [];
}

/// <summary>A batch with stock, for picking the lot a line takes stock from.</summary>
public sealed class BatchStockItem
{
    public long BatchId { get; set; }
    public string BatchNo { get; set; } = string.Empty;
    public DateOnly? ManufactureDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal AverageCost { get; set; }
}

/// <summary>A serial-tracked item in stock.</summary>
public sealed class SerialStockItem
{
    public long SerialNumberId { get; set; }
    public string SerialNo { get; set; } = string.Empty;
    public long? BatchId { get; set; }
}

/// <summary>Everything the inventory forms pick from, in one call.</summary>
public sealed class InventoryOptions
{
    public IReadOnlyList<Unit> Units { get; set; } = [];
    public IReadOnlyList<Category> Categories { get; set; } = [];
    public IReadOnlyList<Brand> Brands { get; set; } = [];
    public IReadOnlyList<Warehouse> Warehouses { get; set; } = [];
    public IReadOnlyList<TaxGroupOption> TaxGroups { get; set; } = [];
}

/// <summary>A tax group with its total rate, for price and tax calculations on forms.</summary>
public sealed class TaxGroupOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public List<TaxGroupRateItem> Rates { get; set; } = [];
}

public sealed class TaxGroupRateItem
{
    public int TaxRateId { get; set; }
    public string TaxCode { get; set; } = string.Empty;
    public string TaxName { get; set; } = string.Empty;
    public string TaxType { get; set; } = string.Empty;
    public decimal RatePercent { get; set; }
    public bool IsRecoverable { get; set; }
    public bool IsCompound { get; set; }
    public int SequenceNo { get; set; }
}
