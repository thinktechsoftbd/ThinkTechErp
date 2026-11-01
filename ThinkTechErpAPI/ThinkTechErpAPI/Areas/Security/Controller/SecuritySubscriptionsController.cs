using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpService.Security;

namespace ThinkTechErpAPI.Areas.Security.Controller;

[ApiController]
[Route("api/security/subscriptions")]
[Authorize(Policy = SecurityPolicies.View)]
[SecurityExceptionFilter]
public sealed class SecuritySubscriptionsController(ISubscriptionService subscriptionService) : ControllerBase
{
    /// <summary>Pass organizationId to list one organization's subscriptions.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<Subscription>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<Subscription>>> GetSubscriptions(
        [FromQuery, Range(1, int.MaxValue)] int? organizationId,
        [FromQuery, Range(1, int.MaxValue)] int? subscriptionModuleId,
        [FromQuery, RegularExpression(SubscriptionRules.StatusPattern)] string? status,
        [FromQuery, RegularExpression(SubscriptionRules.TypePattern)] string? subscriptionType,
        [FromQuery, StringLength(100)] string? search,
        [FromQuery, Range(1, 500)] int limit = 50,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var filter = new SubscriptionFilter
        {
            OrganizationId = organizationId,
            SubscriptionModuleId = subscriptionModuleId,
            Status = status,
            SubscriptionType = subscriptionType,
            Search = search
        };
        return Ok(await subscriptionService.GetSubscriptionsAsync(filter, limit, offset, cancellationToken));
    }

    [HttpGet("{subscriptionId:long}")]
    [ProducesResponseType(typeof(Subscription), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Subscription>> GetSubscription(long subscriptionId, CancellationToken cancellationToken)
    {
        var subscription = await subscriptionService.GetSubscriptionAsync(subscriptionId, cancellationToken);
        return subscription is null ? NotFound() : Ok(subscription);
    }

    /// <summary>
    /// Whether the organization with this Organization Id (object_id) has a valid subscription today:
    /// the organization is active and a subscription has status Active, a start date on or before
    /// today and an end date on or after today (UTC). Pass moduleName to check one module.
    /// Anonymous, so an organization's own installation (e.g. MediDesk) can check before anyone
    /// signs in; the response has no amounts or internal IDs.
    /// </summary>
    [HttpGet("validity/{organizationObjectId:guid}")]
    [AllowAnonymous]
    [EnableRateLimiting(SecurityPolicies.SubscriptionValidityRateLimit)]
    [ProducesResponseType(typeof(SubscriptionValidity), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SubscriptionValidity>> GetSubscriptionValidity(
        Guid organizationObjectId,
        [FromQuery, StringLength(200)] string? moduleName,
        CancellationToken cancellationToken)
    {
        var validity = await subscriptionService.CheckValidityAsync(organizationObjectId, moduleName, cancellationToken);
        return validity is null
            ? NotFound(new { error = "No organization has this Organization Id." })
            : Ok(validity);
    }

    [HttpPost]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(typeof(Subscription), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Subscription>> CreateSubscription(
        [FromBody] SubscriptionUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var subscription = await subscriptionService.CreateSubscriptionAsync(request, userId, cancellationToken);
        return CreatedAtAction(nameof(GetSubscription), new { subscriptionId = subscription.Id }, subscription);
    }

    [HttpPut("{subscriptionId:long}")]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(typeof(Subscription), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Subscription>> UpdateSubscription(
        long subscriptionId,
        [FromBody] SubscriptionUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var subscription = await subscriptionService.UpdateSubscriptionAsync(subscriptionId, request, userId, cancellationToken);
        return subscription is null ? NotFound() : Ok(subscription);
    }

    [HttpDelete("{subscriptionId:long}")]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSubscription(long subscriptionId, CancellationToken cancellationToken)
        => await subscriptionService.DeleteSubscriptionAsync(subscriptionId, cancellationToken)
            ? NoContent()
            : NotFound();
}
