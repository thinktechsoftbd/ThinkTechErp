using System.ComponentModel.DataAnnotations;
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

    /// <summary>
    /// One page of the permissions whose code or name contains <c>search</c>, sorted by <c>sort</c> (code, name or
    /// description) in <c>direction</c> (asc or desc). The full list above stays for the role and user pickers.
    /// </summary>
    [HttpGet("page")]
    [ProducesResponseType(typeof(PagedResult<SecurityPermission>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SecurityPermission>>> SearchPermissions(
        [FromQuery, StringLength(100)] string? search,
        [FromQuery, RegularExpression("^(code|name|description)$")] string sort = "code",
        [FromQuery, RegularExpression("^(asc|desc)$")] string direction = "asc",
        [FromQuery, Range(1, 200)] int limit = 30,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await permissionService.SearchPermissionsAsync(search, sort, direction == "desc", limit, offset, cancellationToken));

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
