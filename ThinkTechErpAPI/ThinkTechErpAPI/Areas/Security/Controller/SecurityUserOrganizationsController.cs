using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpService.Security;

namespace ThinkTechErpAPI.Areas.Security.Controller;

/// <summary>The organizations a user works in (Inventory and Purchase data belongs to one).</summary>
[ApiController]
[SecurityExceptionFilter]
public sealed class SecurityUserOrganizationsController(IUserOrganizationService organizations) : ControllerBase
{
    /// <summary>
    /// The signed-in user's organizations, default first: the ones an X-Organization-Id header may
    /// name. Organization managers (Super Admin) get every active organization.
    /// </summary>
    [HttpGet("api/security/my-organizations")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<UserOrganization>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserOrganization>>> GetMine(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();
        var canManage = User.HasClaim(SecurityPolicies.PermissionClaim, SecurityRules.ManageOrganizationPermission);
        return Ok(await organizations.GetAccessibleAsync(userId, canManage, cancellationToken));
    }

    [HttpGet("api/security/users/{userId:int}/organizations")]
    [Authorize(Policy = SecurityPolicies.View)]
    [ProducesResponseType(typeof(IReadOnlyList<UserOrganization>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<UserOrganization>>> GetForUser(int userId, CancellationToken cancellationToken)
    {
        var linked = await organizations.GetLinkedAsync(userId, cancellationToken);
        return linked is null ? NotFound() : Ok(linked);
    }

    [HttpPut("api/security/users/{userId:int}/organizations")]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(typeof(IReadOnlyList<UserOrganization>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<UserOrganization>>> ReplaceForUser(
        int userId, [FromBody] UserOrganizationsUpdateRequest request, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var actingUserId))
            return Unauthorized();
        var linked = await organizations.ReplaceAsync(userId, request, actingUserId, cancellationToken);
        return linked is null ? NotFound() : Ok(linked);
    }
}
