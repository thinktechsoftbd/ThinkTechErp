using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.ThinkCart;
using ThinkTechErpService.ThinkCart;

namespace ThinkTechErpAPI.Areas.ThinkCart.Controller;

/// <summary>
/// Customer sign-in for the storefront (called by the storefront server with its integration token): register,
/// password or OTP login, password reset by OTP, and rotating refresh tokens. Customer access tokens last 60
/// minutes and work on api/thinkcart only.
/// </summary>
[Route("api/thinkcart/auth")]
[Authorize(Policy = ThinkCartPolicies.Storefront)]
[EnableRateLimiting(SecurityPolicies.AuthRateLimit)]
public sealed class ThinkCartAuthController(IThinkCartAuthService auth) : ThinkCartControllerBase
{
    private const string InvalidCode = "The verification code is wrong or has expired. Ask for a new one.";

    [HttpPost("register")]
    [ProducesResponseType(typeof(CustomerAuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerAuthResponse>> Register(
        [FromBody] CustomerRegisterRequest request,
        CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await auth.RegisterAsync(OrganizationId, request, ClientIp, cancellationToken));

    /// <summary>login is the mobile number or the email.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(CustomerAuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CustomerAuthResponse>> Login(
        [FromBody] CustomerLoginRequest request,
        CancellationToken cancellationToken)
        => AuthResult(await auth.LoginAsync(OrganizationId, request, ClientIp, cancellationToken),
            "Wrong mobile number, email or password.");

    /// <summary>
    /// Sends a code by SMS (purpose login, register or reset_password). 404 when login or reset_password has
    /// no account, 409 when register has one, 429 when asked again within a minute or over 5 times an hour.
    /// </summary>
    [HttpPost("otp/request")]
    [ProducesResponseType(typeof(OtpSendResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<OtpSendResponse>> RequestOtp(
        [FromBody] OtpSendRequest request,
        CancellationToken cancellationToken)
    {
        var result = await auth.RequestOtpAsync(OrganizationId, request, ClientIp, cancellationToken);
        if (result.RetryAfterSeconds > 0)
            Response.Headers.RetryAfter = result.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        return result.Status switch
        {
            OtpSendStatus.Sent => StatusCode(StatusCodes.Status202Accepted,
                new OtpSendResponse { ExpiresInSeconds = result.ExpiresInSeconds }),
            OtpSendStatus.NoAccount => NotFoundError("No account uses this mobile number."),
            OtpSendStatus.AlreadyRegistered => Conflict(new { error = "An account with this mobile number already exists. Sign in instead." }),
            OtpSendStatus.TooSoon => StatusCode(StatusCodes.Status429TooManyRequests,
                new { error = $"Wait {result.RetryAfterSeconds} seconds before asking for another code." }),
            _ => StatusCode(StatusCodes.Status429TooManyRequests,
                new { error = "Too many codes were sent to this number. Try again in an hour." })
        };
    }

    [HttpPost("otp/login")]
    [ProducesResponseType(typeof(CustomerAuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CustomerAuthResponse>> OtpLogin(
        [FromBody] OtpLoginRequest request,
        CancellationToken cancellationToken)
        => AuthResult(await auth.OtpLoginAsync(OrganizationId, request, ClientIp, cancellationToken), InvalidCode);

    /// <summary>Sets a new password with a reset_password code and signs out every session.</summary>
    [HttpPost("password/reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] OtpPasswordResetRequest request,
        CancellationToken cancellationToken)
        => await auth.ResetPasswordAsync(OrganizationId, request, cancellationToken)
            ? NoContent()
            : BadRequest(new { error = InvalidCode });

    [HttpPost("refresh")]
    [ProducesResponseType(typeof(CustomerAuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CustomerAuthResponse>> Refresh(
        [FromBody] CustomerRefreshRequest request,
        CancellationToken cancellationToken)
    {
        var response = await auth.RefreshAsync(OrganizationId, request.RefreshToken, ClientIp, cancellationToken);
        return response is null
            ? Unauthorized(new { error = "The refresh token is invalid or expired. Sign in again." })
            : Ok(response);
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] CustomerRefreshRequest request)
    {
        // Deliberately not tied to the request's cancellation: see IThinkCartAuthService.LogoutAsync.
        await auth.LogoutAsync(request.RefreshToken);
        return NoContent();
    }

    private ActionResult<CustomerAuthResponse> AuthResult(CustomerAuthResult result, string invalidMessage)
        => result.Status switch
        {
            CustomerAuthStatus.Succeeded => Ok(result.Response),
            CustomerAuthStatus.Blocked => StatusCode(StatusCodes.Status403Forbidden,
                new { error = "This account is blocked. Please call us." }),
            _ => Unauthorized(new { error = invalidMessage })
        };
}
