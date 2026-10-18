using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpService.Security;

namespace ThinkTechErpAPI.Areas.Security.Controller;

[ApiController]
[Route("api/security/organizations")]
[Authorize(Policy = SecurityPolicies.View)]
[SecurityExceptionFilter]
public sealed class SecurityOrganizationsController(IOrganizationService organizationService) : ControllerBase
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
    [Authorize(Policy = SecurityPolicies.Manage)]
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
    [Authorize(Policy = SecurityPolicies.Manage)]
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
}
