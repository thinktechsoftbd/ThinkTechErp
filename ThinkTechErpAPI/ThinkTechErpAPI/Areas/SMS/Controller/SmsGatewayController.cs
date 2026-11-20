using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Sms;
using ThinkTechErpService.Sms;

namespace ThinkTechErpAPI.Areas.SMS.Controller;

/// <summary>
/// Sends SMS and reads the account at a provider's gateway. {providerId} is the provider's sms_provider_id
/// (SmsProviderConstants: CDC = 6, ADN = 5); the provider must be active, and its endpoint (api_base_url) and key
/// (api_key) are read from public.sms_provider on every call. 400: the request or the provider's settings need
/// correcting, the provider has no gateway client yet, or the gateway refused the request (its reason is in error);
/// 502: the gateway can't be reached, refused the key or answered unexpectedly.
/// </summary>
[Route("api/sms/gateway/{providerId:int}")]
[Authorize(Policy = SmsPolicies.View)]
public sealed class SmsGatewayController(ISmsGatewayService gateway) : ErpControllerBase
{
    /// <summary>Sends the message to every recipient. It is charged by the provider.</summary>
    [HttpPost("send")]
    [Authorize(Policy = SmsPolicies.Manage)]
    public async Task<ActionResult<SmsSendResult>> Send(int providerId, [FromBody] SmsSendRequest request, CancellationToken cancellationToken)
        => Ok(await gateway.SendAsync(providerId, request, cancellationToken));

    /// <summary>The wallet balance and credit at the provider.</summary>
    [HttpGet("balance")]
    public async Task<ActionResult<SmsGatewayBalance>> GetBalance(int providerId, CancellationToken cancellationToken)
        => Ok(await gateway.GetBalanceAsync(providerId, cancellationToken));

    /// <summary>The messages sent through the provider, a page at a time as it lists them; limit up to 100.</summary>
    [HttpGet("messages")]
    public async Task<ActionResult<SmsMessageHistory>> GetMessageHistory(
        int providerId,
        [FromQuery, Range(1, 1_000_000)] int page = 1,
        [FromQuery, Range(1, SmsHistoryRules.MaxLimit)] int limit = SmsHistoryRules.DefaultLimit,
        CancellationToken cancellationToken = default)
        => Ok(await gateway.GetMessageHistoryAsync(providerId, page, limit, cancellationToken));

    /// <summary>Delivery of a message sent earlier; {messageId} is the MessageId the send returned.</summary>
    [HttpGet("messages/{messageId}/status")]
    public async Task<ActionResult<SmsMessageStatus>> GetMessageStatus(
        int providerId, [StringLength(100)] string messageId, CancellationToken cancellationToken)
        => Ok(await gateway.GetMessageStatusAsync(providerId, messageId, cancellationToken));
}
