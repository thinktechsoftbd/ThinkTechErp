using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpService.Security;

namespace ThinkTechErpAPI.Areas.Security.Controller;

[ApiController]
[Route("api/security/users")]
[Authorize(Policy = SecurityPolicies.View)]
[SecurityExceptionFilter]
public sealed class SecurityUsersController(ISecurityUserService userService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<SecurityUser>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SecurityUser>>> GetUsers(
        [FromQuery, StringLength(100)] string? search,
        [FromQuery, RegularExpression(SecurityRules.UserStatusPattern)] string? status,
        [FromQuery, Range(1, 500)] int limit = 50,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await userService.GetUsersAsync(search, status, limit, offset, cancellationToken));

    [HttpGet("{userId:int}")]
    [ProducesResponseType(typeof(SecurityUser), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SecurityUser>> GetUser(int userId, CancellationToken cancellationToken)
    {
        var user = await userService.GetUserAsync(userId, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPost]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(typeof(SecurityUser), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SecurityUser>> CreateUser(
        [FromBody] UserCreateRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userService.CreateUserAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetUser), new { userId = user.Id }, user);
    }

    [HttpPut("{userId:int}")]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(typeof(SecurityUser), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SecurityUser>> UpdateUser(
        int userId,
        [FromBody] UserUpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var actingUserId))
            return Unauthorized();

        var user = await userService.UpdateUserAsync(userId, request, actingUserId, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPut("{userId:int}/password")]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetPassword(
        int userId,
        [FromBody] SetPasswordRequest request,
        CancellationToken cancellationToken)
        => await userService.SetPasswordAsync(userId, request.NewPassword, cancellationToken)
            ? NoContent()
            : NotFound();

    [HttpGet("{userId:int}/permissions")]
    [ProducesResponseType(typeof(UserPermissionSet), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserPermissionSet>> GetPermissions(int userId, CancellationToken cancellationToken)
    {
        var permissions = await userService.GetPermissionsAsync(userId, cancellationToken);
        return permissions is null ? NotFound() : Ok(permissions);
    }

    [HttpPut("{userId:int}/permissions")]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(typeof(UserPermissionSet), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserPermissionSet>> SetPermissions(
        int userId,
        [FromBody] PermissionAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        var permissions = await userService.SetPermissionsAsync(userId, request.PermissionIds, cancellationToken);
        return permissions is null ? NotFound() : Ok(permissions);
    }

    [HttpGet("{userId:int}/menus")]
    [ProducesResponseType(typeof(IReadOnlyList<UserMenuAccess>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<UserMenuAccess>>> GetMenus(int userId, CancellationToken cancellationToken)
    {
        var menus = await userService.GetMenusAsync(userId, cancellationToken);
        return menus is null ? NotFound() : Ok(menus);
    }

    [HttpPut("{userId:int}/menus")]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(typeof(IReadOnlyList<UserMenuAccess>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<UserMenuAccess>>> SetMenus(
        int userId,
        [FromBody] UserMenuAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        var menus = await userService.SetMenusAsync(userId, request.Menus, cancellationToken);
        return menus is null ? NotFound() : Ok(menus);
    }
}
