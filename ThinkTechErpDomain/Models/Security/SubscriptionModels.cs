using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.Security;

public static class SubscriptionRules
{
    public const string TypePattern = "^(Free Trial|Premium)$";
    public const string BillingPeriodPattern = "^(Monthly|Yearly)$";
    public const string StatusPattern = "^(Pending|Active|Expired|Cancelled)$";
}

public static class SubscriptionStatuses
{
    public const string Pending = "Pending";
    public const string Active = "Active";
    public const string Expired = "Expired";
    public const string Cancelled = "Cancelled";
}

public sealed class SubscriptionModule
{
    public int Id { get; set; }
    public Guid ObjectId { get; set; }
    public string ModuleName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? Remarks { get; set; }
    public int SubscriptionCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}

public sealed class SubscriptionModuleUpsertRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string ModuleName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    [StringLength(1000)]
    public string? Remarks { get; set; }
}

public sealed class Subscription
{
    public long Id { get; set; }
    public int OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public int SubscriptionModuleId { get; set; }
    public string ModuleName { get; set; } = string.Empty;
    public string SubscriptionType { get; set; } = string.Empty;
    public string BillingPeriodType { get; set; } = string.Empty;
    public int BillingPeriodCount { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public long CurrencyLookupDataId { get; set; }
    public string CurrencyName { get; set; } = string.Empty;
    public decimal LocalExchangeRate { get; set; }
    public decimal SubtotalAmount { get; set; }
    public decimal DiscountPercentage { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}

public sealed class SubscriptionFilter
{
    public int? OrganizationId { get; set; }
    public int? SubscriptionModuleId { get; set; }
    public string? Status { get; set; }
    public string? SubscriptionType { get; set; }

    /// <summary>Matches the organization or module name.</summary>
    public string? Search { get; set; }
}

public sealed class SubscriptionUpsertRequest
{
    [Range(1, int.MaxValue)]
    public int OrganizationId { get; set; }

    [Range(1, int.MaxValue)]
    public int SubscriptionModuleId { get; set; }

    [Required, RegularExpression(SubscriptionRules.TypePattern, ErrorMessage = "Subscription type must be Free Trial or Premium.")]
    public string SubscriptionType { get; set; } = string.Empty;

    [Required, RegularExpression(SubscriptionRules.BillingPeriodPattern, ErrorMessage = "Billing period must be Monthly or Yearly.")]
    public string BillingPeriodType { get; set; } = string.Empty;

    [Range(1, 120)]
    public int BillingPeriodCount { get; set; } = 1;

    [Required]
    public DateOnly? StartDate { get; set; }

    /// <summary>Omit to calculate it: start date + billing period × count − 1 day.</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>A value of the "Currency" lookup table.</summary>
    [Range(1, long.MaxValue)]
    public long CurrencyLookupDataId { get; set; }

    [Range(typeof(decimal), "0.000001", "999999999999")]
    public decimal LocalExchangeRate { get; set; } = 1;

    [Range(typeof(decimal), "0", "9999999999999999")]
    public decimal SubtotalAmount { get; set; }

    /// <summary>When above 0, the discount amount is calculated from it.</summary>
    [Range(typeof(decimal), "0", "100")]
    public decimal DiscountPercentage { get; set; }

    /// <summary>Used only when DiscountPercentage is 0.</summary>
    [Range(typeof(decimal), "0", "9999999999999999")]
    public decimal DiscountAmount { get; set; }

    [Range(typeof(decimal), "0", "9999999999999999")]
    public decimal VatAmount { get; set; }

    [Required, RegularExpression(SubscriptionRules.StatusPattern, ErrorMessage = "Status must be Pending, Active, Expired or Cancelled.")]
    public string Status { get; set; } = SubscriptionStatuses.Active;
}
