using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ThinkTechErpDomain.Models.Sms;

// Sending SMS through a provider's gateway (api/sms/gateway/{providerId}). The provider's endpoint and
// credentials are read from public.sms_provider on every call, so nothing about a provider is configured in code.

public static class SmsServiceTypes
{
    public const string NonMasking = "non_masking_sms";
    public const string Masking = "masking_sms";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { NonMasking, Masking };
}

/// <summary>An SMS to send to one or more mobile numbers.</summary>
public sealed class SmsSendRequest
{
    /// <summary>non_masking_sms (sent from a number) or masking_sms (sent under a brand name).</summary>
    [Required, StringLength(30)]
    public string ServiceType { get; set; } = string.Empty;

    [Required, StringLength(1600, MinimumLength = 1)]
    public string Message { get; set; } = string.Empty;

    /// <summary>Mobile numbers, e.g. 01712345678 or 8801712345678.</summary>
    [Required, MinLength(1, ErrorMessage = "Add at least one recipient."), MaxLength(1000)]
    public List<string> Recipients { get; set; } = [];

    /// <summary>The number or brand to send from; empty: the provider's default.</summary>
    [StringLength(50)]
    public string? SenderId { get; set; }
}

public sealed class SmsSendResult
{
    public int ProviderId { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    /// <summary>The provider's id of the message, for its status.</summary>
    public string? MessageId { get; set; }
    public int TotalSent { get; set; }
    public int SmsPartsPerMessage { get; set; }
    /// <summary>What the provider says is left after this message, when it says.</summary>
    public decimal? RemainingBalance { get; set; }
}

/// <summary>The account balance at the provider (not the SMS bought and allocated in this ERP).</summary>
public sealed class SmsGatewayBalance
{
    public int ProviderId { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public decimal? WalletBalance { get; set; }
    public decimal? CreditLimit { get; set; }
    public decimal? AvailableCredit { get; set; }
}

public sealed class SmsMessageStatus
{
    public int ProviderId { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    /// <summary>The provider's status, e.g. completed.</summary>
    public string? Status { get; set; }
    public int Delivered { get; set; }
    public int Failed { get; set; }
}

public sealed class SmsMessageHistory
{
    public int ProviderId { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Page { get; set; }
    public int Limit { get; set; }
    /// <summary>The messages as the provider lists them.</summary>
    public IReadOnlyList<JsonElement> Messages { get; set; } = [];
}

public static class SmsHistoryRules
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;
}

/// <summary>
/// A provider's row with its secrets, for calling its gateway only. Never returned by the API or logged
/// (ToString leaves the secrets out).
/// </summary>
public sealed class SmsGatewayProvider
{
    public int ProviderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? ApiBaseUrl { get; set; }
    public string? ApiUsername { get; set; }
    public string? ApiPassword { get; set; }
    public string? ApiKey { get; set; }
    public bool IsActive { get; set; }

    public override string ToString() => $"{Name} ({Code}, id {ProviderId})";
}

/// <summary>
/// The provider's gateway can't be reached, refused this API's credentials or answered with something
/// unexpected. Answered as 502: it is not the caller's mistake.
/// </summary>
public sealed class SmsGatewayException(string message, Exception? innerException = null)
    : Exception(message, innerException);
