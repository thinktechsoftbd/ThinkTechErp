using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.Security;

/// <summary>A subscription plan (public.subscription_plan) with its features and their details.</summary>
public sealed class SubscriptionPlan
{
    public int Id { get; set; }
    public Guid ObjectId { get; set; }
    public string NameEn { get; set; } = string.Empty;
    public string? NameLocal { get; set; }
    public string? ShortNote { get; set; }
    public decimal RegularPrice { get; set; }
    public decimal DiscountPercentage { get; set; }
    /// <summary>The price after the discount.</summary>
    public decimal DiscountPrice { get; set; }
    /// <summary>A value of the "Currency" lookup table (rf_currency_id).</summary>
    public long CurrencyLookupDataId { get; set; }
    public string CurrencyName { get; set; } = string.Empty;
    public decimal LocalExchangeRate { get; set; }
    /// <summary>Monthly or Yearly.</summary>
    public string BillingPeriodType { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int FeatureCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
    public List<SubscriptionPlanFeature> Features { get; set; } = [];
}

/// <summary>A feature of a plan (public.subscription_plan_feature_h).</summary>
public sealed class SubscriptionPlanFeature
{
    public int Id { get; set; }
    public Guid ObjectId { get; set; }
    public int SubscriptionPlanId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public List<SubscriptionPlanFeatureDetail> Details { get; set; } = [];
}

/// <summary>A detail line of a feature (public.subscription_plan_feature_d).</summary>
public sealed class SubscriptionPlanFeatureDetail
{
    public int Id { get; set; }
    public Guid ObjectId { get; set; }
    public int FeatureId { get; set; }
    public string Description { get; set; } = string.Empty;
}

public sealed class SubscriptionPlanUpsertRequest
{
    [Required, StringLength(150, MinimumLength = 1)]
    public string NameEn { get; set; } = string.Empty;

    [StringLength(150)] public string? NameLocal { get; set; }
    [StringLength(500)] public string? ShortNote { get; set; }

    [Range(typeof(decimal), "0", "9999999999999999")]
    public decimal RegularPrice { get; set; }

    /// <summary>When above 0, the discount price is calculated from it.</summary>
    [Range(typeof(decimal), "0", "100")]
    public decimal DiscountPercentage { get; set; }

    /// <summary>The price after the discount; used only when DiscountPercentage is 0 (the percentage is then calculated).</summary>
    [Range(typeof(decimal), "0", "9999999999999999")]
    public decimal? DiscountPrice { get; set; }

    /// <summary>A value of the "Currency" lookup table.</summary>
    [Range(1, long.MaxValue)]
    public long CurrencyLookupDataId { get; set; }

    [Range(typeof(decimal), "0.000001", "999999999999")]
    public decimal LocalExchangeRate { get; set; } = 1;

    [Required, RegularExpression(SubscriptionRules.BillingPeriodPattern, ErrorMessage = "Billing period must be Monthly or Yearly.")]
    public string BillingPeriodType { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>The plan's features in order. Saved ones keep their id and are updated in place; the rest are removed.</summary>
    public List<SubscriptionPlanFeatureRequest> Features { get; set; } = [];
}

public sealed class SubscriptionPlanFeatureRequest
{
    /// <summary>A saved feature of this plan; null for a new one.</summary>
    [Range(1, int.MaxValue)] public int? Id { get; set; }

    [Required, StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public List<SubscriptionPlanFeatureDetailRequest> Details { get; set; } = [];
}

public sealed class SubscriptionPlanFeatureDetailRequest
{
    /// <summary>A saved detail of this feature; null for a new one.</summary>
    [Range(1, int.MaxValue)] public int? Id { get; set; }

    [Required, StringLength(500, MinimumLength = 1)]
    public string Description { get; set; } = string.Empty;
}
