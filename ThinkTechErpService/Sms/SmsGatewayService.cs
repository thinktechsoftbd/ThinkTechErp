using System.Text.RegularExpressions;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Sms;
using ThinkTechErpRepository.Sms;
using ThinkTechErpService.Common;

namespace ThinkTechErpService.Sms;

/// <summary>
/// Sends SMS and reads the account at a provider's gateway. Every call names the provider (sms_provider_id); its
/// endpoint and credentials are read from public.sms_provider each time, so a provider's settings take effect at once.
/// </summary>
public interface ISmsGatewayService
{
    Task<SmsSendResult> SendAsync(int providerId, SmsSendRequest request, CancellationToken cancellationToken = default);
    Task<SmsGatewayBalance> GetBalanceAsync(int providerId, CancellationToken cancellationToken = default);
    Task<SmsMessageStatus> GetMessageStatusAsync(int providerId, string messageId, CancellationToken cancellationToken = default);
    Task<SmsMessageHistory> GetMessageHistoryAsync(int providerId, int page, int limit, CancellationToken cancellationToken = default);
}

/// <summary>
/// Errors: BusinessRuleException (400) when the request or the provider's settings need correcting, the provider
/// isn't active or has no client yet, or the gateway refused the request with a reason; SmsGatewayException (502)
/// when the gateway can't be reached, refuses the key or answers unexpectedly.
/// </summary>
public sealed partial class SmsGatewayService(ISmsGatewayRepository repository, IEnumerable<ISmsGatewayClient> clients)
    : ISmsGatewayService
{
    public async Task<SmsSendResult> SendAsync(int providerId, SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        var message = Normalize(request);
        var (client, connection) = await ConnectAsync(providerId, cancellationToken);
        var result = await client.SendAsync(connection, message, cancellationToken);
        result.ProviderId = connection.ProviderId;
        result.ProviderCode = connection.Code;
        return result;
    }

    public async Task<SmsGatewayBalance> GetBalanceAsync(int providerId, CancellationToken cancellationToken = default)
    {
        var (client, connection) = await ConnectAsync(providerId, cancellationToken);
        var balance = await client.GetBalanceAsync(connection, cancellationToken);
        balance.ProviderId = connection.ProviderId;
        balance.ProviderCode = connection.Code;
        return balance;
    }

    public async Task<SmsMessageStatus> GetMessageStatusAsync(int providerId, string messageId, CancellationToken cancellationToken = default)
    {
        var id = Text.Clean(messageId) ?? throw new BusinessRuleException("Enter the message id the provider gave when it was sent.");
        if (id.Length > 100)
            throw new BusinessRuleException("The message id is too long.");
        var (client, connection) = await ConnectAsync(providerId, cancellationToken);
        var status = await client.GetMessageStatusAsync(connection, id, cancellationToken);
        status.ProviderId = connection.ProviderId;
        status.ProviderCode = connection.Code;
        if (string.IsNullOrEmpty(status.MessageId))
            status.MessageId = id;
        return status;
    }

    public async Task<SmsMessageHistory> GetMessageHistoryAsync(int providerId, int page, int limit, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        limit = limit < 1 ? SmsHistoryRules.DefaultLimit : Math.Min(limit, SmsHistoryRules.MaxLimit);
        var (client, connection) = await ConnectAsync(providerId, cancellationToken);
        var history = await client.GetMessageHistoryAsync(connection, page, limit, cancellationToken);
        history.ProviderId = connection.ProviderId;
        history.ProviderCode = connection.Code;
        return history;
    }

    private async Task<(ISmsGatewayClient Client, SmsGatewayConnection Connection)> ConnectAsync(
        int providerId, CancellationToken cancellationToken)
    {
        var provider = await repository.GetProviderAsync(providerId, cancellationToken)
            ?? throw new BusinessRuleException($"There is no SMS provider {providerId}.");
        if (!provider.IsActive)
            throw new BusinessRuleException($"The SMS provider {provider.Name} isn't active. Activate it in its settings first.");

        var client = clients.FirstOrDefault(c => c.ProviderId == provider.ProviderId)
            ?? throw new BusinessRuleException($"Sending SMS through {provider.Name} ({provider.Code}) isn't supported yet.");

        var baseUrl = Text.Clean(provider.ApiBaseUrl)
            ?? throw new BusinessRuleException($"{provider.Name} has no API base URL. Set it in the SMS provider's settings.");
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("https" or "http"))
            throw new BusinessRuleException($"{provider.Name}'s API base URL isn't an http(s) address. Correct it in the SMS provider's settings.");

        return (client, new SmsGatewayConnection
        {
            ProviderId = provider.ProviderId,
            Name = provider.Name,
            Code = provider.Code,
            Endpoint = endpoint,
            ApiKey = Text.Clean(provider.ApiKey),
            ApiUsername = Text.Clean(provider.ApiUsername),
            ApiPassword = Text.Clean(provider.ApiPassword)
        });
    }

    /// <summary>Checks the message and recipients (digits only, each once) before any provider is called.</summary>
    private static SmsSendRequest Normalize(SmsSendRequest request)
    {
        var serviceType = Text.Clean(request.ServiceType)?.ToLowerInvariant();
        if (serviceType is null || !SmsServiceTypes.All.Contains(serviceType))
            throw new BusinessRuleException($"ServiceType must be {SmsServiceTypes.NonMasking} or {SmsServiceTypes.Masking}.");

        var message = request.Message?.Trim();
        if (string.IsNullOrEmpty(message))
            throw new BusinessRuleException("Enter the message.");

        var recipients = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var invalid = new List<string>();
        foreach (var recipient in request.Recipients ?? [])
        {
            var number = NumberSeparators().Replace(recipient ?? string.Empty, string.Empty);
            if (!MobileNumber().IsMatch(number))
                invalid.Add(string.IsNullOrWhiteSpace(recipient) ? "(empty)" : recipient.Trim());
            else if (seen.Add(number))
                recipients.Add(number);
        }

        if (invalid.Count > 0)
            throw new BusinessRuleException(
                $"These recipients aren't mobile numbers: {string.Join(", ", invalid.Take(5))}{(invalid.Count > 5 ? ", …" : "")}.");
        if (recipients.Count == 0)
            throw new BusinessRuleException("Add at least one recipient.");

        return new SmsSendRequest
        {
            ServiceType = serviceType,
            Message = message,
            Recipients = recipients,
            SenderId = Text.Clean(request.SenderId)
        };
    }

    // Spaces, dashes, dots, brackets and a leading + are dropped: +880 1712-345678 → 8801712345678.
    [GeneratedRegex(@"[\s\-().+]")]
    private static partial Regex NumberSeparators();

    [GeneratedRegex(@"^\d{10,15}$")]
    private static partial Regex MobileNumber();
}
