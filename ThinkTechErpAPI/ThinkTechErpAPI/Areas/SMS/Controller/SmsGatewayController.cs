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
/// 502: the gateway can't be reached, refused the key or answered unexpectedly. Users need SMS.V (SMS.M to send);
/// integration clients (e.g. MediDesk) need the sms.usage scope and an organization (org_object_id), whose
/// sms_usage their sends add to.
/// </summary>
[Route("api/sms/gateway/{providerId:int}")]
[Authorize(Policy = SmsPolicies.Gateway)]
public sealed class SmsGatewayController(
    ISmsGatewayService gateway, ISmsService sms, IntegrationClientStore clients, ILogger<SmsGatewayController> logger)
    : ErpControllerBase
{
    /// <summary>
    /// Sends the message to every recipient. It is charged by the provider. For an integration client, the SMS sent
    /// (one per recipient and message part) are added to its organization's sms_usage.
    /// </summary>
    [HttpPost("send")]
    [Authorize(Policy = SmsPolicies.GatewaySend)]
    public async Task<ActionResult<SmsSendResult>> Send(int providerId, [FromBody] SmsSendRequest request, CancellationToken cancellationToken)
    {
        if (!IsIntegrationClient)
            return Ok(await gateway.SendAsync(providerId, request, cancellationToken));

        if (await ClientOrganizationAsync(cancellationToken) is not Guid organization)
            return NoOrganization();

        // Not cancelled with the client's request, so what the provider sends is always added to the usage.
        var result = await gateway.SendAsync(providerId, request, CancellationToken.None);
        var smsCount = SentSmsCount(result, request);
        try
        {
            if (!await sms.AddSentSmsAsync(organization, smsCount, CancellationToken.None))
                logger.LogError("Sent {SmsCount} SMS for organization {Organization}, which no longer exists.", smsCount, organization);
        }
        catch (Exception exception)
        {
            // Sent already: failing the request would only make the client send them again.
            logger.LogError(exception, "Sent {SmsCount} SMS for organization {Organization} but couldn't add them to its usage.",
                smsCount, organization);
        }
        return Ok(result);
    }

    /// <summary>
    /// For a user: the wallet balance and credit at the provider. For an integration client: its organization's SMS
    /// balance instead (SMS allocated to it − sms_usage).
    /// </summary>
    [HttpGet("balance")]
    public async Task<ActionResult<SmsGatewayBalance>> GetBalance(int providerId, CancellationToken cancellationToken)
    {
        if (!IsIntegrationClient)
            return Ok(await gateway.GetBalanceAsync(providerId, cancellationToken));

        var balance = await ClientOrganizationAsync(cancellationToken) is Guid organization
            ? await sms.GetOrganizationBalanceAsync(organization, cancellationToken)
            : null;
        if (balance is null)
            return NoOrganization();
        return Ok(new SmsGatewayBalance
        {
            ProviderId = providerId,
            OrganizationObjectId = balance.ObjectId,
            AllocatedSms = balance.AllocatedSms,
            SmsUsage = balance.SmsUsage,
            SmsBalance = balance.AvailableSms
        });
    }

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

    // Users' tokens carry no client_id
    private bool IsIntegrationClient => User.HasClaim(claim => claim.Type == IntegrationPolicies.ClientIdClaim);

    private async Task<Guid?> ClientOrganizationAsync(CancellationToken cancellationToken)
        => (await clients.FindAsync(User.FindFirst(IntegrationPolicies.ClientIdClaim)?.Value, cancellationToken))?.OrgObjectId;

    private ObjectResult NoOrganization()
        => StatusCode(StatusCodes.Status403Forbidden,
            new { error = "This API client isn't linked to an organization. Set its organization in Security → Clients." });

    // One per SMS: each recipient and each part of a long message, as the provider counted them
    private static long SentSmsCount(SmsSendResult result, SmsSendRequest request)
    {
        var recipients = result.TotalSent > 0 ? result.TotalSent : request.Recipients.Distinct().Count();
        return (long)recipients * Math.Max(1, result.SmsPartsPerMessage);
    }
}
