using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpService.Security;

namespace ThinkTechErpAPI.Areas.Security.Controller;

[ApiController]
[Route("api/security/organizations")]
[Authorize(Policy = OrganizationPolicies.View)]
[SecurityExceptionFilter]
public sealed class SecurityOrganizationsController(
    IOrganizationService organizationService,
    ISubscriptionService subscriptionService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<Organization>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<Organization>>> GetOrganizations(CancellationToken cancellationToken)
        => Ok(await organizationService.GetOrganizationsAsync(cancellationToken));

    [HttpGet("{organizationId:int}")]
    [ProducesResponseType(typeof(Organization), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Organization>> GetOrganization(int organizationId, CancellationToken cancellationToken)
    {
        var organization = await organizationService.GetOrganizationAsync(organizationId, cancellationToken);
        return organization is null ? NotFound() : Ok(organization);
    }

    [HttpPost]
    [Authorize(Policy = OrganizationPolicies.Manage)]
    [ProducesResponseType(typeof(Organization), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Organization>> CreateOrganization(
        [FromBody] OrganizationUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var organization = await organizationService.CreateOrganizationAsync(request, userId, cancellationToken);
        return CreatedAtAction(nameof(GetOrganization), new { organizationId = organization.Id }, organization);
    }

    [HttpPut("{organizationId:int}")]
    [Authorize(Policy = OrganizationPolicies.Manage)]
    [ProducesResponseType(typeof(Organization), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Organization>> UpdateOrganization(
        int organizationId,
        [FromBody] OrganizationUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var organization = await organizationService.UpdateOrganizationAsync(
            organizationId, request, userId, cancellationToken);
        return organization is null ? NotFound() : Ok(organization);
    }

    /// <summary>
    /// Whether the organization with this Organization Id (object_id) has a valid subscription today:
    /// the organization is active and a subscription has status Active, a start date on or before
    /// today and an end date on or after today (UTC). Pass moduleName to check one module.
    /// Anonymous, so an organization's own installation can check before anyone signs in; the
    /// response has no amounts or internal IDs.
    /// </summary>
    [HttpGet("{objectId:guid}/subscription-validity")]
    [AllowAnonymous]
    [EnableRateLimiting(SecurityPolicies.SubscriptionValidityRateLimit)]
    [ProducesResponseType(typeof(SubscriptionValidity), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SubscriptionValidity>> GetSubscriptionValidity(
        Guid objectId,
        [FromQuery, StringLength(200)] string? moduleName,
        CancellationToken cancellationToken)
    {
        var validity = await subscriptionService.CheckValidityAsync(objectId, moduleName, cancellationToken);
        return validity is null
            ? NotFound(new { error = "No organization has this Organization Id." })
            : Ok(validity);
    }
}
