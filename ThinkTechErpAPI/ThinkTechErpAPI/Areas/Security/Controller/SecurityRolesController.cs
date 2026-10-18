using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpService.Security;

namespace ThinkTechErpAPI.Areas.Security.Controller;

[ApiController]
[Route("api/security/roles")]
[Authorize(Policy = SecurityPolicies.View)]
[SecurityExceptionFilter]
public sealed class SecurityRolesController(ISecurityRoleService roleService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SecurityRole>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SecurityRole>>> GetRoles(CancellationToken cancellationToken)
        => Ok(await roleService.GetRolesAsync(cancellationToken));

    [HttpGet("{roleId:int}")]
    [ProducesResponseType(typeof(SecurityRole), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SecurityRole>> GetRole(int roleId, CancellationToken cancellationToken)
    {
        var role = await roleService.GetRoleAsync(roleId, cancellationToken);
        return role is null ? NotFound() : Ok(role);
    }

    [HttpPost]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(typeof(SecurityRole), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SecurityRole>> CreateRole(
        [FromBody] RoleUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var role = await roleService.CreateRoleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetRole), new { roleId = role.Id }, role);
    }

    [HttpPut("{roleId:int}")]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(typeof(SecurityRole), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SecurityRole>> UpdateRole(
        int roleId,
        [FromBody] RoleUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var role = await roleService.UpdateRoleAsync(roleId, request, cancellationToken);
        return role is null ? NotFound() : Ok(role);
    }

    [HttpDelete("{roleId:int}")]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteRole(int roleId, CancellationToken cancellationToken)
        => await roleService.DeleteRoleAsync(roleId, cancellationToken) switch
        {
            DeleteOutcome.Deleted => NoContent(),
            DeleteOutcome.NotFound => NotFound(),
            _ => Conflict(new { error = "The role is still assigned to users. Remove it from them first." })
        };
}
