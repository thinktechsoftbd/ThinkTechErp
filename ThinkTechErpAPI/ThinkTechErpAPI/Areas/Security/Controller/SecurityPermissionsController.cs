using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpService.Security;

namespace ThinkTechErpAPI.Areas.Security.Controller;

[ApiController]
[Route("api/security/permissions")]
[Authorize(Policy = SecurityPolicies.View)]
[SecurityExceptionFilter]
public sealed class SecurityPermissionsController(ISecurityPermissionService permissionService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SecurityPermission>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SecurityPermission>>> GetPermissions(CancellationToken cancellationToken)
        => Ok(await permissionService.GetPermissionsAsync(cancellationToken));

    [HttpGet("{permissionId:int}")]
    [ProducesResponseType(typeof(SecurityPermission), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SecurityPermission>> GetPermission(int permissionId, CancellationToken cancellationToken)
    {
        var permission = await permissionService.GetPermissionAsync(permissionId, cancellationToken);
        return permission is null ? NotFound() : Ok(permission);
    }

    [HttpPost]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(typeof(SecurityPermission), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SecurityPermission>> CreatePermission(
        [FromBody] PermissionUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var permission = await permissionService.CreatePermissionAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetPermission), new { permissionId = permission.Id }, permission);
    }

    [HttpPut("{permissionId:int}")]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(typeof(SecurityPermission), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SecurityPermission>> UpdatePermission(
        int permissionId,
        [FromBody] PermissionUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var permission = await permissionService.UpdatePermissionAsync(permissionId, request, cancellationToken);
        return permission is null ? NotFound() : Ok(permission);
    }

    [HttpDelete("{permissionId:int}")]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeletePermission(int permissionId, CancellationToken cancellationToken)
        => await permissionService.DeletePermissionAsync(permissionId, cancellationToken) switch
        {
            DeleteOutcome.Deleted => NoContent(),
            DeleteOutcome.NotFound => NotFound(),
            _ => Conflict(new { error = "The permission is still granted to roles or users. Remove it from them first." })
        };
}
