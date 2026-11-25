using ThinkTechErpDomain.Models.Sms;

namespace ThinkTechErpService.Sms;

/// <summary>
/// Talks to one provider's SMS gateway. Each provider has its own implementation (CDC now; ADN to come), registered
/// in DependencyInjection.cs; SmsGatewayService picks the one whose ProviderId is the calling organization's provider.
/// </summary>
public interface ISmsGatewayClient
{
    /// <summary>The sms_provider_id it handles (SmsProviderConstants).</summary>
    int ProviderId { get; }

    Task<SmsSendResult> SendAsync(SmsGatewayConnection connection, SmsSendRequest request, CancellationToken cancellationToken = default);
    Task<SmsGatewayBalance> GetBalanceAsync(SmsGatewayConnection connection, CancellationToken cancellationToken = default);
    Task<SmsMessageStatus> GetMessageStatusAsync(SmsGatewayConnection connection, string messageId, CancellationToken cancellationToken = default);
    Task<SmsMessageHistory> GetMessageHistoryAsync(SmsGatewayConnection connection, int page, int limit, CancellationToken cancellationToken = default);
}

/// <summary>
/// An active provider's gateway settings from public.sms_provider, its endpoint checked, for one call. The secrets
/// are whatever the provider stores; each client checks for the ones it needs. ToString leaves them out.
/// </summary>
public sealed class SmsGatewayConnection
{
    public required int ProviderId { get; init; }
    public required string Name { get; init; }
    public required string Code { get; init; }
    /// <summary>api_base_url, an absolute http(s) address.</summary>
    public required Uri Endpoint { get; init; }
    public string? ApiKey { get; init; }
    public string? ApiUsername { get; init; }
    public string? ApiPassword { get; init; }
    /// <summary>sms_sender_id: the number or brand name to send from (always set for a send).</summary>
    public string? SenderId { get; init; }

    public override string ToString() => $"{Name} ({Code}, id {ProviderId})";
}
