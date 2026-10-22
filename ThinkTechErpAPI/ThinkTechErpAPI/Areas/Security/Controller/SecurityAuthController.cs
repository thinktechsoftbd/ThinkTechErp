using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpService.Security;

namespace ThinkTechErpAPI.Areas.Security.Controller;

[ApiController]
[Route("api/security/auth")]
[Authorize]
[SecurityExceptionFilter]
public sealed class SecurityAuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(SecurityPolicies.AuthRateLimit)]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, ClientIp, cancellationToken);
        return result.Status switch
        {
            LoginStatus.Succeeded => Ok(result.Response),
            LoginStatus.Inactive => StatusCode(StatusCodes.Status403Forbidden, new { error = "This account is inactive." }),
            _ => Unauthorized(new { error = "Invalid email or password." })
        };
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(SecurityPolicies.AuthRateLimit)]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var response = await authService.RefreshAsync(request.RefreshToken, ClientIp, cancellationToken);
        return response is null
            ? Unauthorized(new { error = "The refresh token is invalid or expired. Sign in again." })
            : Ok(response);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
    {
        // Deliberately not tied to the request's cancellation: see IAuthService.LogoutAsync.
        await authService.LogoutAsync(request.RefreshToken);
        return NoContent();
    }

    [HttpPost("logout-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutEverywhere()
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        await authService.LogoutEverywhereAsync(userId);
        return NoContent();
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(CurrentUser), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUser>> GetCurrentUser(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var user = await authService.GetCurrentUserAsync(userId, cancellationToken);
        return user is null ? Unauthorized() : Ok(user);
    }

    [HttpGet("menus")]
    [ProducesResponseType(typeof(IReadOnlyList<MenuNode>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MenuNode>>> GetMenus(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        return Ok(await authService.GetMenuTreeAsync(userId, cancellationToken));
    }

    [HttpPost("change-password")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponse>> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var response = await authService.ChangePasswordAsync(userId, request, ClientIp, cancellationToken);
        return response is null
            ? BadRequest(new { error = "The current password is incorrect." })
            : Ok(response);
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(SecurityPolicies.AuthRateLimit)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await authService.RequestPasswordResetAsync(request.Email, cancellationToken);

        // Same answer whether or not the account exists.
        return Accepted(new { message = "If the email belongs to an active account, a reset token has been sent." });
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(SecurityPolicies.AuthRateLimit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
        => await authService.ResetPasswordAsync(request, cancellationToken)
            ? NoContent()
            : BadRequest(new { error = "The reset token is invalid or has expired." });

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
}
