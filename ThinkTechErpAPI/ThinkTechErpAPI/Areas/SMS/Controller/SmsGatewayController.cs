using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Sms;
using ThinkTechErpService.Security;
using ThinkTechErpService.Sms;

namespace ThinkTechErpAPI.Areas.SMS.Controller;

/// <summary>
/// Sends SMS and reads the account at the caller's organization's provider gateway. The caller names neither the
/// provider nor the sender: the organization is an integration client's own (org_object_id) or, for a user, the
/// X-Organization-Id header's (or their default), its provider is organization.sms_provider_id (Security →
/// Organizations), and the provider's endpoint (api_base_url), key (api_key) and sender (sms_sender_id) are read from
/// public.sms_provider on every call. 400: the request or the settings need correcting (no provider, an inactive one,
/// no sender ID to send, no gateway client yet), or the gateway refused the request (its reason is in error); 502: the
/// gateway can't be reached, refused the key or answered unexpectedly. Users need SMS.V (SMS.M to send); integration
/// clients (e.g. MediDesk) need the sms.usage scope and an organization, whose sms_usage their sends add to.
/// <para>
/// api/sms/gateway/{providerId}/... is the old route, kept for MediDesk copies deployed before the provider moved to
/// the organization: {providerId} is ignored (the organization's provider is used), as is a senderId in the body.
/// Remove it once every MediDesk calls api/sms/gateway.
/// </para>
/// </summary>
[Route("api/sms/gateway")]
[Route(LegacyRoute)]
[Authorize(Policy = SmsPolicies.Gateway)]
public sealed class SmsGatewayController(
    ISmsGatewayService gateway, ISmsService sms, IntegrationClientStore clients, IUserOrganizationService userOrganizations,
    IOrganizationService organizations, ILogger<SmsGatewayController> logger)
    : ErpControllerBase
{
    private const string LegacyRoute = "api/sms/gateway/{providerId:int}";

    /// <summary>
    /// Sends the message to every recipient. It is charged by the provider. For an integration client, the SMS sent
    /// (one per recipient and message part) are added to its organization's sms_usage.
    /// </summary>
    [HttpPost("send")]
    [Authorize(Policy = SmsPolicies.GatewaySend)]
    public async Task<ActionResult<SmsSendResult>> Send([FromBody] SmsSendRequest request, CancellationToken cancellationToken)
    {
        var (organization, refusal) = await CallerOrganizationAsync(cancellationToken);
        if (organization is not Guid objectId)
            return refusal!;
        if (!IsIntegrationClient)
            return Ok(await gateway.SendAsync(objectId, request, cancellationToken));

        // Not cancelled with the client's request, so what the provider sends is always added to the usage.
        var result = await gateway.SendAsync(objectId, request, CancellationToken.None);
        var smsCount = SentSmsCount(result, request);
        try
        {
            if (!await sms.AddSentSmsAsync(objectId, smsCount, CancellationToken.None))
                logger.LogError("Sent {SmsCount} SMS for organization {Organization}, which no longer exists.", smsCount, objectId);
        }
        catch (Exception exception)
        {
            // Sent already: failing the request would only make the client send them again.
            logger.LogError(exception, "Sent {SmsCount} SMS for organization {Organization} but couldn't add them to its usage.",
                smsCount, objectId);
        }
        return Ok(result);
    }

    /// <summary>
    /// For a user: the wallet balance and credit at the organization's provider. For an integration client: its
    /// organization's SMS balance instead (SMS allocated to it − sms_usage), which needs no gateway call.
    /// </summary>
    [HttpGet("balance")]
    public async Task<ActionResult<SmsGatewayBalance>> GetBalance(CancellationToken cancellationToken)
    {
        var (organization, refusal) = await CallerOrganizationAsync(cancellationToken);
        if (organization is not Guid objectId)
            return refusal!;
        if (!IsIntegrationClient)
            return Ok(await gateway.GetBalanceAsync(objectId, cancellationToken));

        var balance = await sms.GetOrganizationBalanceAsync(objectId, cancellationToken);
        if (balance is null)
            return NoOrganization();
        return Ok(new SmsGatewayBalance
        {
            ProviderId = balance.ProviderId,
            ProviderCode = balance.ProviderCode,
            OrganizationObjectId = balance.ObjectId,
            AllocatedSms = balance.AllocatedSms,
            SmsUsage = balance.SmsUsage,
            SmsBalance = balance.AvailableSms
        });
    }

    /// <summary>The messages sent through the organization's provider, a page at a time as it lists them; limit up to 100.</summary>
    [HttpGet("messages")]
    public async Task<ActionResult<SmsMessageHistory>> GetMessageHistory(
        [FromQuery, Range(1, 1_000_000)] int page = 1,
        [FromQuery, Range(1, SmsHistoryRules.MaxLimit)] int limit = SmsHistoryRules.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var (organization, refusal) = await CallerOrganizationAsync(cancellationToken);
        if (organization is not Guid objectId)
            return refusal!;
        return Ok(await gateway.GetMessageHistoryAsync(objectId, page, limit, cancellationToken));
    }

    /// <summary>Delivery of a message sent earlier; {messageId} is the MessageId the send returned.</summary>
    [HttpGet("messages/{messageId}/status")]
    public async Task<ActionResult<SmsMessageStatus>> GetMessageStatus(
        [StringLength(100)] string messageId, CancellationToken cancellationToken)
    {
        var (organization, refusal) = await CallerOrganizationAsync(cancellationToken);
        if (organization is not Guid objectId)
            return refusal!;
        return Ok(await gateway.GetMessageStatusAsync(objectId, messageId, cancellationToken));
    }

    // Users' tokens carry no client_id
    private bool IsIntegrationClient => User.HasClaim(claim => claim.Type == IntegrationPolicies.ClientIdClaim);

    /// <summary>The organization the call is for (its Object Id); otherwise the answer to give.</summary>
    private async Task<(Guid? Organization, ActionResult? Refusal)> CallerOrganizationAsync(CancellationToken cancellationToken)
    {
        if (RouteData.Values.TryGetValue("providerId", out var providerId))
            logger.LogInformation(
                "The old SMS gateway route ({Route}) was called with provider {ProviderId}, which is ignored: the organization's provider is used.",
                LegacyRoute, providerId);

        if (IsIntegrationClient)
        {
            var client = await clients.FindAsync(User.FindFirst(IntegrationPolicies.ClientIdClaim)?.Value, cancellationToken);
            return client?.OrgObjectId is Guid clientOrganization ? (clientOrganization, null) : (null, NoOrganization());
        }

        var (organizationId, refusal) = await OrganizationScopeFilter.ResolveAsync(HttpContext, userOrganizations);
        if (refusal is not null)
            return (null, refusal);
        var organization = await organizations.GetOrganizationAsync(organizationId!.Value, cancellationToken);
        return organization is null
            ? (null, StatusCode(StatusCodes.Status403Forbidden, new { error = "Your organization doesn't exist any more." }))
            : (organization.ObjectId, null);
    }

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
