using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.Sms;

public static class SmsRules
{
    public const string ViewSmsPermission = "SMS.V";
    public const string ManageSmsPermission = "SMS.M";

    /// <summary>The local currency purchases are converted to (local_exchange_rate, local_total_amount).</summary>
    public const string LocalCurrency = "BDT";
}

// ---- Providers

/// <summary>An SMS gateway. Its API password and key are never sent back; HasApiPassword/HasApiKey say whether they are set.</summary>
public sealed class SmsProvider
{
    public int Id { get; set; }
    public Guid ObjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? ApiBaseUrl { get; set; }
    public string? ApiUsername { get; set; }
    public bool HasApiPassword { get; set; }
    public bool HasApiKey { get; set; }
    public bool IsActive { get; set; }
    public long PurchasedSms { get; set; }
    public long AllocatedSms { get; set; }
    /// <summary>Bought from the provider and not yet allocated to an organization.</summary>
    public long AvailableSms { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class SmsProviderUpsertRequest
{
    [Required, StringLength(150, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(50, MinimumLength = 1)]
    public string Code { get; set; } = string.Empty;

    [StringLength(500), Url]
    public string? ApiBaseUrl { get; set; }

    [StringLength(150)]
    public string? ApiUsername { get; set; }

    /// <summary>Null keeps the stored password; an empty string removes it.</summary>
    [StringLength(1000)]
    public string? ApiPassword { get; set; }

    /// <summary>Null keeps the stored key; an empty string removes it.</summary>
    [StringLength(2000)]
    public string? ApiKey { get; set; }

    public bool IsActive { get; set; } = true;
}

// ---- Purchases

public sealed class SmsPurchase
{
    public long Id { get; set; }
    public Guid ObjectId { get; set; }
    public int ProviderId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string ProviderCode { get; set; } = string.Empty;
    public DateOnly PurchaseDate { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? ReferenceNumber { get; set; }
    public long SmsQuantity { get; set; }
    public int CurrencyId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal TotalAmount { get; set; }
    /// <summary>BDT per unit of the purchase currency.</summary>
    public decimal LocalExchangeRate { get; set; }
    /// <summary>The total in BDT when bought.</summary>
    public decimal LocalTotalAmount { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class SmsPurchaseUpsertRequest
{
    [Range(1, int.MaxValue)]
    public int ProviderId { get; set; }

    public DateOnly PurchaseDate { get; set; }

    [StringLength(100)] public string? InvoiceNumber { get; set; }
    [StringLength(150)] public string? ReferenceNumber { get; set; }

    [Range(1, long.MaxValue, ErrorMessage = "Enter how many SMS were bought.")]
    public long SmsQuantity { get; set; }

    /// <summary>A value of the Currency lookup list.</summary>
    [Range(1, int.MaxValue)]
    public int CurrencyId { get; set; }

    [Range(0, 999_999_999)]
    public decimal UnitPrice { get; set; }

    /// <summary>The invoice total; empty: quantity x unit price.</summary>
    [Range(0, 999_999_999_999)]
    public decimal? TotalAmount { get; set; }

    /// <summary>BDT per unit of the currency; ignored (1) for BDT.</summary>
    [Range(typeof(decimal), "0.000001", "999999999")]
    public decimal LocalExchangeRate { get; set; } = 1;

    [StringLength(2000)] public string? Remarks { get; set; }
}

// ---- Allocations

public sealed class SmsAllocation
{
    public long Id { get; set; }
    public Guid ObjectId { get; set; }
    public int ProviderId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string ProviderCode { get; set; } = string.Empty;
    /// <summary>The organization's Object Id (sms_allocation.organization_id).</summary>
    public Guid OrganizationObjectId { get; set; }
    public int OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public string? OrganizationCode { get; set; }
    public long AllocatedSms { get; set; }
    public DateOnly AllocationDate { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class SmsAllocationUpsertRequest
{
    [Range(1, int.MaxValue)]
    public int ProviderId { get; set; }

    public Guid OrganizationObjectId { get; set; }

    [Range(1, long.MaxValue, ErrorMessage = "Enter how many SMS to allocate.")]
    public long AllocatedSms { get; set; }

    public DateOnly AllocationDate { get; set; }

    [StringLength(2000)] public string? Remarks { get; set; }
}

// ---- Organization balances and usage

public sealed class SmsOrganizationBalance
{
    public int OrganizationId { get; set; }
    public Guid ObjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool IsActive { get; set; }
    public long AllocatedSms { get; set; }
    public long SmsUsage { get; set; }
    public long AvailableSms { get; set; }
    public int AllocationCount { get; set; }
    public DateOnly? LastAllocationDate { get; set; }
}

/// <summary>SMS sent by an organization, added to its usage (only within what it has left).</summary>
public sealed class SmsUsageRequest
{
    [Range(1, 10_000_000, ErrorMessage = "Enter how many SMS were sent.")]
    public long SmsCount { get; set; }
}

/// <summary>A correction of an organization's total usage (e.g. after a provider's report).</summary>
public sealed class SmsUsageCorrectionRequest
{
    [Range(0, long.MaxValue)]
    public long SmsUsage { get; set; }
}

/// <summary>What an integration client sees of its organization's SMS balance.</summary>
public sealed class SmsBalance
{
    public Guid OrganizationObjectId { get; set; }
    public long AllocatedSms { get; set; }
    public long SmsUsage { get; set; }
    public long AvailableSms { get; set; }
}

public sealed class SmsOverview
{
    public int ProviderCount { get; set; }
    public int ActiveProviderCount { get; set; }
    public long PurchasedSms { get; set; }
    public long AllocatedSms { get; set; }
    /// <summary>Bought and not yet allocated, all providers.</summary>
    public long UnallocatedSms { get; set; }
    public long UsedSms { get; set; }
    /// <summary>Allocated to organizations and not yet used.</summary>
    public long OrganizationAvailableSms { get; set; }
    public int OrganizationCount { get; set; }
    /// <summary>What all purchases cost, in BDT.</summary>
    public decimal PurchaseSpend { get; set; }
    public IReadOnlyList<SmsProvider> Providers { get; set; } = [];
    /// <summary>Organizations with an allocation, the lowest balance first.</summary>
    public IReadOnlyList<SmsOrganizationBalance> LowestBalances { get; set; } = [];
}

/// <summary>Filters of the purchase and allocation lists.</summary>
public sealed class SmsListQuery
{
    public string? Search { get; set; }
    public int? ProviderId { get; set; }
    public Guid? OrganizationObjectId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Limit { get; set; } = 20;
    public int Offset { get; set; }
}
