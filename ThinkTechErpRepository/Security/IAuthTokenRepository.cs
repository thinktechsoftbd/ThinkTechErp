using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpRepository.Security;

/// <summary>Refresh and password-reset tokens. Callers pass SHA-256 hashes, never the tokens themselves.</summary>
public interface IAuthTokenRepository
{
    /// <returns>The UTC expiry of the new refresh token.</returns>
    Task<DateTime> CreateRefreshTokenAsync(int userId, string tokenHash, TimeSpan lifetime, string? createdByIp, CancellationToken cancellationToken = default);
    Task<RefreshTokenRecord?> GetRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Revokes an active refresh token and issues its replacement in one transaction.</summary>
    /// <returns>The UTC expiry of the replacement, or null when the old token was no longer active.</returns>
    Task<DateTime?> RotateRefreshTokenAsync(int tokenId, string newTokenHash, TimeSpan lifetime, string? createdByIp, CancellationToken cancellationToken = default);
    Task RevokeRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task RevokeAllRefreshTokensAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Stores a new reset token and invalidates the user's earlier unused ones.</summary>
    Task CreatePasswordResetTokenAsync(int userId, string tokenHash, TimeSpan lifetime, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes an unused, unexpired reset token of an active user, sets the new password hash and
    /// revokes the user's refresh tokens, all in one transaction.
    /// </summary>
    /// <returns>The user's ID, or null when the token is invalid.</returns>
    Task<int?> ResetPasswordAsync(string tokenHash, string newPasswordHash, CancellationToken cancellationToken = default);
}
