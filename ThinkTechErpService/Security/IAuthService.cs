using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpService.Security;

public enum LoginStatus
{
    Succeeded,
    InvalidCredentials,
    Inactive
}

public sealed record LoginResult(LoginStatus Status, AuthResponse? Response = null);

public interface IAuthService
{
    Task<LoginResult> LoginAsync(LoginRequest request, string? clientIp, CancellationToken cancellationToken = default);

    /// <returns>A new token pair, or null when the refresh token is unknown, expired, revoked or its user inactive.</returns>
    Task<AuthResponse?> RefreshAsync(string refreshToken, string? clientIp, CancellationToken cancellationToken = default);
    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task LogoutEverywhereAsync(int userId, CancellationToken cancellationToken = default);
    Task<CurrentUser?> GetCurrentUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MenuNode>> GetMenuTreeAsync(int userId, CancellationToken cancellationToken = default);

    /// <returns>A fresh token pair (every other session is signed out), or null when the current password is wrong.</returns>
    Task<AuthResponse?> ChangePasswordAsync(int userId, ChangePasswordRequest request, string? clientIp, CancellationToken cancellationToken = default);
    Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
}
