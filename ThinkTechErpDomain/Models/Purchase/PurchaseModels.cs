using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.Purchase;

public static class PurchaseRules
{
    public const string ViewPurchasePermission = "PURCHASE.V";
    public const string ManagePurchasePermission = "PURCHASE.M";

    public const string SupplierGroupLookupTable = "Supplier Group";

    public static readonly IReadOnlySet<string> TaxTypes = new HashSet<string>(StringComparer.Ordinal)
        { "VAT", "GST", "SalesTax", "SD", "Excise", "Customs", "Other" };
}

// ---- Suppliers

public sealed class Supplier
{
    public int Id { get; set; }
    public Guid ObjectId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameLocal { get; set; }
    public long? SupplierGroupLookupDataId { get; set; }
    public string? SupplierGroup { get; set; }
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
    public string? TaxRegistrationNo { get; set; }
    public string? TaxIdNo { get; set; }
    public string? TradeLicenseNo { get; set; }
    public string? RegulatoryLicenseNo { get; set; }
    public long? CurrencyLookupDataId { get; set; }
    public string? Currency { get; set; }
    public int PaymentTermsDays { get; set; }
    public decimal? CreditLimit { get; set; }
    public decimal OpeningBalance { get; set; }
    public DateOnly? OpeningBalanceDate { get; set; }
    public string? BankName { get; set; }
    public string? BankBranch { get; set; }
    public string? BankAccountName { get; set; }
    public string? BankAccountNo { get; set; }
    public string? BankRoutingCode { get; set; }
    public string? MobileWalletNo { get; set; }
    public string? Remarks { get; set; }
    public bool IsActive { get; set; }
    /// <summary>Also a customer: the same party is listed on the sales screens.</summary>
    public bool IsCustomer { get; set; }
    /// <summary>Owed to the supplier now (+), or advanced to them (-), in the base currency.</summary>
    public decimal Balance { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class SupplierUpsertRequest
{
    [Required, StringLength(30, MinimumLength = 1)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(250, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(250)] public string? NameLocal { get; set; }
    [Range(1, long.MaxValue)] public long? SupplierGroupLookupDataId { get; set; }
    [StringLength(150)] public string? ContactPerson { get; set; }
    [StringLength(30)] public string? Phone { get; set; }
    [StringLength(30)] public string? Mobile { get; set; }
    [StringLength(150), EmailAddress] public string? Email { get; set; }
    [StringLength(250)] public string? AddressLine1 { get; set; }
    [StringLength(250)] public string? AddressLine2 { get; set; }
    [StringLength(100)] public string? City { get; set; }
    [StringLength(100)] public string? State { get; set; }
    [StringLength(20)] public string? PostalCode { get; set; }
    [StringLength(2, MinimumLength = 2)] public string? CountryCode { get; set; }
    [StringLength(50)] public string? TaxRegistrationNo { get; set; }
    [StringLength(50)] public string? TaxIdNo { get; set; }
    [StringLength(50)] public string? TradeLicenseNo { get; set; }
    [StringLength(100)] public string? RegulatoryLicenseNo { get; set; }
    [Range(1, long.MaxValue)] public long? CurrencyLookupDataId { get; set; }
    [Range(0, 3650)] public int PaymentTermsDays { get; set; }
    [Range(0, 999_999_999_999)] public decimal? CreditLimit { get; set; }
    [Range(-999_999_999_999, 999_999_999_999)] public decimal OpeningBalance { get; set; }
    public DateOnly? OpeningBalanceDate { get; set; }
    [StringLength(150)] public string? BankName { get; set; }
    [StringLength(150)] public string? BankBranch { get; set; }
    [StringLength(150)] public string? BankAccountName { get; set; }
    [StringLength(50)] public string? BankAccountNo { get; set; }
    [StringLength(30)] public string? BankRoutingCode { get; set; }
    [StringLength(30)] public string? MobileWalletNo { get; set; }
    [StringLength(1000)] public string? Remarks { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Also a customer; their credit terms are set on the sales Customers screen.</summary>
    public bool IsCustomer { get; set; }
}

/// <summary>A supplier as a document picks it.</summary>
public sealed class SupplierOption
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Mobile { get; set; }
    public int PaymentTermsDays { get; set; }
    public long? CurrencyLookupDataId { get; set; }
    public decimal Balance { get; set; }
    public bool IsActive { get; set; }
}

// ---- Taxes

public sealed class TaxRate
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TaxType { get; set; } = "VAT";
    public decimal RatePercent { get; set; }
    public bool IsRecoverable { get; set; }
    public bool IsActive { get; set; }
    public int GroupCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class TaxRateUpsertRequest
{
    [Required, StringLength(30, MinimumLength = 1)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(150, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string TaxType { get; set; } = "VAT";

    [Range(0, 1000)]
    public decimal RatePercent { get; set; }

    public bool IsRecoverable { get; set; } = true;
    public bool IsActive { get; set; } = true;
}

public sealed class TaxGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    /// <summary>The tax on 100 at these rates (compound rates included), e.g. 15 for VAT 15%.</summary>
    public decimal EffectiveRatePercent { get; set; }
    public List<Inventory.TaxGroupRateItem> Rates { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class TaxGroupUpsertRequest
{
    [Required, StringLength(150, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>In order; an empty list makes an exempt group.</summary>
    public List<TaxGroupRateRequest> Rates { get; set; } = [];
}

public sealed class TaxGroupRateRequest
{
    [Range(1, int.MaxValue)]
    public int TaxRateId { get; set; }

    public bool IsCompound { get; set; }
}
