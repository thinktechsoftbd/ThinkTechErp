using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ThinkTechErpCore.Helpers;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Sms;

namespace ThinkTechErpService.Sms;

/// <summary>
/// CDC's A2P gateway. Every call is a JSON POST to the provider's api_base_url (e.g.
/// https://a2p.cdclbd.net/functions/v1/transactional-sms) with its api_key as the bearer token. A body without
/// "action" sends SMS; "check-balance", "message-status" and "message-history" read the account. Answers carry
/// "success". The key is only ever put in the Authorization header: never in a message or a log.
/// </summary>
public sealed class CdcSmsGatewayClient(HttpClient http, ILogger<CdcSmsGatewayClient> logger) : ISmsGatewayClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public int ProviderId => SmsProviderConstants.CDC;

    public async Task<SmsSendResult> SendAsync(
        SmsGatewayConnection connection, SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        var answer = await PostAsync<SendAnswer>(connection, "send the SMS", new SendBody
        {
            ServiceType = request.ServiceType,
            Message = request.Message,
            Recipients = request.Recipients,
            SenderId = connection.SenderId
        }, cancellationToken);
        return new SmsSendResult
        {
            MessageId = answer.MessageId,
            TotalSent = answer.TotalSent ?? 0,
            SmsPartsPerMessage = answer.SmsPartsPerMessage ?? 0,
            RemainingBalance = answer.RemainingBalance
        };
    }

    public async Task<SmsGatewayBalance> GetBalanceAsync(SmsGatewayConnection connection, CancellationToken cancellationToken = default)
    {
        var answer = await PostAsync<BalanceAnswer>(connection, "check the balance",
            new ActionBody { Action = "check-balance" }, cancellationToken);
        return new SmsGatewayBalance
        {
            WalletBalance = answer.WalletBalance,
            CreditLimit = answer.CreditLimit,
            AvailableCredit = answer.AvailableCredit
        };
    }

    public async Task<SmsMessageStatus> GetMessageStatusAsync(
        SmsGatewayConnection connection, string messageId, CancellationToken cancellationToken = default)
    {
        var answer = await PostAsync<StatusAnswer>(connection, "get the message status",
            new ActionBody { Action = "message-status", MessageId = messageId }, cancellationToken);
        return new SmsMessageStatus
        {
            MessageId = messageId,
            Status = answer.Status,
            Delivered = answer.Delivered ?? 0,
            Failed = answer.Failed ?? 0
        };
    }

    public async Task<SmsMessageHistory> GetMessageHistoryAsync(
        SmsGatewayConnection connection, int page, int limit, CancellationToken cancellationToken = default)
    {
        var answer = await PostAsync<HistoryAnswer>(connection, "list the messages",
            new ActionBody { Action = "message-history", Page = page, Limit = limit }, cancellationToken);
        return new SmsMessageHistory
        {
            Total = answer.Total ?? 0,
            Page = answer.Page ?? page,
            Limit = limit,
            Messages = answer.Messages ?? []
        };
    }

    private async Task<T> PostAsync<T>(SmsGatewayConnection connection, string action, object body, CancellationToken cancellationToken)
        where T : Answer
    {
        var apiKey = connection.ApiKey
            ?? throw new BusinessRuleException($"{connection.Name} has no API key. Set it in the SMS provider's settings.");

        // Sent as bytes, so the gateway gets a Content-Length rather than a chunked body.
        var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), Json));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, connection.Endpoint) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException
                                          || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(exception, "Couldn't {Action} through {Provider}: its gateway can't be reached.", action, connection);
            throw new SmsGatewayException($"{connection.Name}'s SMS gateway can't be reached right now. Try again in a moment.", exception);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            var status = (int)response.StatusCode;

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                logger.LogWarning("{Provider} refused the API key (HTTP {Status}) when asked to {Action}.", connection, status, action);
                throw new SmsGatewayException(
                    $"{connection.Name} refused this API's credentials (HTTP {status}). Check the provider's API key.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var error = ErrorText(text, apiKey);
                logger.LogWarning("{Provider} answered HTTP {Status} when asked to {Action}: {Error}",
                    connection, status, action, error ?? "(no reason given)");
                // The gateway refused this request and said why (e.g. a bad number, too little balance): the caller can act on it.
                if (error is not null && status is >= 400 and < 500 and not (408 or 429))
                    throw new BusinessRuleException($"{connection.Name} refused to {action}: {error}");
                throw new SmsGatewayException(
                    $"{connection.Name}'s SMS gateway couldn't {action} (HTTP {status}{(error is null ? "" : $": {error}")}). Try again later.");
            }

            T? answer = null;
            Exception? parseError = null;
            try
            {
                answer = string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize<T>(text, Json);
            }
            catch (JsonException exception)
            {
                parseError = exception;
            }

            if (answer?.Success is null)
            {
                logger.LogError(parseError, "{Provider} answered HTTP {Status} to {Action} without its usual JSON.", connection, status, action);
                throw new SmsGatewayException($"{connection.Name}'s SMS gateway gave an answer this API doesn't understand.");
            }

            if (answer.Success is false)
            {
                var error = ErrorText(text, apiKey);
                logger.LogWarning("{Provider} refused to {Action}: {Error}", connection, action, error ?? "(no reason given)");
                throw new BusinessRuleException($"{connection.Name} refused to {action}: {error ?? "it gave no reason."}");
            }

            return answer;
        }
    }

    /// <summary>The gateway's reason ("error", "message", ...) from a JSON answer, shortened, with the key masked just in case.</summary>
    private static string? ErrorText(string text, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            foreach (var name in (string[])["error", "message", "msg", "detail", "errors"])
            {
                if (!document.RootElement.TryGetProperty(name, out var value))
                    continue;
                var reason = value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Object when value.TryGetProperty("message", out var inner) && inner.ValueKind == JsonValueKind.String
                        => inner.GetString(),
                    JsonValueKind.Array or JsonValueKind.Object => value.GetRawText(),
                    _ => null
                };
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    reason = reason.Replace(apiKey, "***", StringComparison.Ordinal).Trim();
                    return reason.Length > 300 ? reason[..300] + "…" : reason;
                }
            }
            return null;
        }
        catch (JsonException)
        {
            // Not JSON (e.g. an HTML error page): nothing worth repeating.
            return null;
        }
    }

    // ---- CDC's request and answer bodies (camelCase on the wire)

    private sealed class SendBody
    {
        public string ServiceType { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public IReadOnlyList<string> Recipients { get; init; } = [];
        public string? SenderId { get; init; }
    }

    private sealed class ActionBody
    {
        public string Action { get; init; } = string.Empty;
        public string? MessageId { get; init; }
        public int? Page { get; init; }
        public int? Limit { get; init; }
    }

    private abstract class Answer
    {
        public bool? Success { get; set; }
    }

    private sealed class SendAnswer : Answer
    {
        public string? MessageId { get; set; }
        public int? TotalSent { get; set; }
        public int? SmsPartsPerMessage { get; set; }
        public decimal? RemainingBalance { get; set; }
    }

    private sealed class BalanceAnswer : Answer
    {
        public decimal? WalletBalance { get; set; }
        public decimal? CreditLimit { get; set; }
        public decimal? AvailableCredit { get; set; }
    }

    private sealed class StatusAnswer : Answer
    {
        public string? Status { get; set; }
        public int? Delivered { get; set; }
        public int? Failed { get; set; }
    }

    private sealed class HistoryAnswer : Answer
    {
        public int? Total { get; set; }
        public int? Page { get; set; }
        public List<JsonElement>? Messages { get; set; }
    }
}
