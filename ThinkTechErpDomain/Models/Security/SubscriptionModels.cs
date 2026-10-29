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

/// <summary>
/// Whether an organization's subscription is valid today. Kept free of amounts and internal IDs:
/// it is returned to callers that have not signed in.
/// </summary>
public sealed class SubscriptionValidity
{
    public Guid OrganizationObjectId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;

    /// <summary>True when at least one subscription counts (see <see cref="Subscriptions"/>).</summary>
    public bool IsValid { get; set; }

    /// <summary>Why the subscription isn't valid; null when it is.</summary>
    public string? Reason { get; set; }

    /// <summary>The date checked against: today in UTC.</summary>
    public DateOnly CheckedOn { get; set; }

    /// <summary>The latest end date of the subscriptions that count.</summary>
    public DateOnly? ValidUntil { get; set; }

    /// <summary>Days from <see cref="CheckedOn"/> to <see cref="ValidUntil"/>; 0 on the last valid day.</summary>
    public int? DaysRemaining { get; set; }

    /// <summary>Subscriptions with status Active whose start date is on or before today and end date on or after it.</summary>
    public CurrentSubscription[] Subscriptions { get; set; } = [];
}

public sealed class CurrentSubscription
{
    public string ModuleName { get; set; } = string.Empty;
    public string SubscriptionType { get; set; } = string.Empty;
    public string BillingPeriodType { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int DaysRemaining { get; set; }
}

/// <summary>The organization fields a subscription check needs.</summary>
public sealed class SubscriptionOrganization
{
    public int Id { get; set; }
    public Guid ObjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
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
