using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.Sms;

public sealed class SmsClient
{
    public string ClientId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public long AvailableSms { get; set; }
    public long TotalAllocatedSms { get; set; }
    public long TotalConsumedSms { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class SmsPlatformBalance
{
    public long TotalPurchasedSms { get; set; }
    public long UnallocatedSms { get; set; }
    public long AssignedAvailableSms { get; set; }
    public long ConsumedSms { get; set; }
    public long TotalAvailableSms { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class SmsAdnPurchase
{
    public long PurchaseId { get; set; }
    public long TransactionId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? ProviderReference { get; set; }
    public long SmsQuantity { get; set; }
    public decimal? AmountPaid { get; set; }
    public string? Currency { get; set; }
    public decimal? ExchangeRate { get; set; }
    public DateTimeOffset PurchasedAt { get; set; }
    public string? Note { get; set; }
}

public sealed class SmsClientAllocation
{
    public long AllocationId { get; set; }
    public long TransactionId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public long SmsQuantity { get; set; }
    public string? Note { get; set; }
    public bool IsFinalized { get; set; }
    public DateTimeOffset AllocatedAt { get; set; }
}

public sealed class SmsUsageRecord
{
    public long UsageId { get; set; }
    public long TransactionId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string RequestReference { get; set; } = string.Empty;
    public string? ProviderMessageId { get; set; }
    public string? Recipient { get; set; }
    public long SmsQuantity { get; set; }
    public DateTimeOffset SentAt { get; set; }
}

public sealed class SmsBalanceTransaction
{
    public long TransactionId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public long Quantity { get; set; }
    public string? ClientId { get; set; }
    public long PlatformDelta { get; set; }
    public long ClientDelta { get; set; }
    public long PlatformBalanceAfter { get; set; }
    public long? ClientBalanceAfter { get; set; }
    public string? ProviderReference { get; set; }
    public string? RequestReference { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SmsPurchaseRequest
{
    [Range(1, long.MaxValue)]
    public long SmsQuantity { get; set; }

    [StringLength(128)]
    public string? ProviderReference { get; set; }

    [Range(typeof(decimal), "0", "999999999999.99")]
    public decimal? AmountPaid { get; set; }

    [RegularExpression("^[A-Z]{3}$")]
    public string? Currency { get; set; }

    [Range(typeof(decimal), "0.000001", "999999999999.999999")]
    public decimal? ExchangeRate { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}

public sealed class SmsPurchaseUpdateRequest
{
    [Required, StringLength(128, MinimumLength = 1)]
    public string ProviderReference { get; set; } = string.Empty;

    [Range(typeof(decimal), "0", "999999999999.99")]
    public decimal AmountPaid { get; set; }

    [Required, RegularExpression("^(BDT|USD)$")]
    public string Currency { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.000001", "999999999999.999999")]
    public decimal? ExchangeRate { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}
public sealed class SmsClientCreateRequest
{
    [Required, StringLength(128, MinimumLength = 1)]
    public string ClientId { get; set; } = string.Empty;

    [Required, StringLength(200, MinimumLength = 1)]
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class SmsClientUpdateRequest
{
    [StringLength(200, MinimumLength = 1)]
    public string? DisplayName { get; set; }

    public bool? IsActive { get; set; }
}

public sealed class SmsAllocationRequest
{
    [Range(1, long.MaxValue)]
    public long SmsQuantity { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    public bool IsFinalized { get; set; }
}

public sealed class SmsAllocationUpdateRequest
{
    [StringLength(500)]
    public string? Note { get; set; }
}

public sealed class SmsUsageRequest
{
    [Range(1, long.MaxValue)]
    public long SmsQuantity { get; set; }

    [Required, StringLength(128, MinimumLength = 1)]
    public string RequestReference { get; set; } = string.Empty;

    [StringLength(32)]
    public string? Recipient { get; set; }

    [StringLength(128)]
    public string? ProviderMessageId { get; set; }
}
