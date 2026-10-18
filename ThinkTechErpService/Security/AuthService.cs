using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpService.Security;

public sealed class AuthService(
    ISecurityUserRepository users,
    IAuthTokenRepository tokens,
    IPasswordHasher passwordHasher,
    IAccessTokenIssuer accessTokenIssuer,
    IPasswordResetNotifier passwordResetNotifier,
    SecurityTokenSettings settings) : IAuthService
{
    private const int MaxIpLength = 45;

    public async Task<LoginResult> LoginAsync(
        LoginRequest request, string? clientIp, CancellationToken cancellationToken = default)
    {
        var user = await users.GetCredentialsByEmailAsync(request.Email.Trim(), cancellationToken);

        // Verify even when the email is unknown so both cases take the same time.
        if (!passwordHasher.Verify(request.Password, user?.PasswordHash) || user is null)
            return new LoginResult(LoginStatus.InvalidCredentials);

        if (user.Status != UserStatuses.Active)
            return new LoginResult(LoginStatus.Inactive);

        return new LoginResult(LoginStatus.Succeeded, await StartSessionAsync(user, clientIp, cancellationToken));
    }

    public async Task<AuthResponse?> RefreshAsync(
        string refreshToken, string? clientIp, CancellationToken cancellationToken = default)
    {
        var record = await tokens.GetRefreshTokenAsync(OpaqueToken.Hash(refreshToken), cancellationToken);
        if (record is null || record.IsExpired)
            return null;

        if (record.IsRevoked)
        {
            // A rotated-out token came back. Unless a parallel refresh (another tab) rotated it
            // moments ago, it may have been stolen, so end every session of that user.
            if (!record.WasJustRotated)
                await tokens.RevokeAllRefreshTokensAsync(record.UserId, cancellationToken);

            return null;
        }

        var user = await users.GetCredentialsByIdAsync(record.UserId, cancellationToken);
        if (user is null || user.Status != UserStatuses.Active)
        {
            await tokens.RevokeAllRefreshTokensAsync(record.UserId, cancellationToken);
            return null;
        }

        var newRefreshToken = OpaqueToken.Create();
        var refreshExpiresAt = await tokens.RotateRefreshTokenAsync(
            record.Id, OpaqueToken.Hash(newRefreshToken), settings.RefreshTokenLifetime, TrimIp(clientIp),
            cancellationToken);
        return refreshExpiresAt is null
            ? null
            : await BuildResponseAsync(user, newRefreshToken, refreshExpiresAt.Value, cancellationToken);
    }

    public Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
        => tokens.RevokeRefreshTokenAsync(OpaqueToken.Hash(refreshToken), cancellationToken);

    public Task LogoutEverywhereAsync(int userId, CancellationToken cancellationToken = default)
        => tokens.RevokeAllRefreshTokensAsync(userId, cancellationToken);

    public async Task<CurrentUser?> GetCurrentUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await users.GetCredentialsByIdAsync(userId, cancellationToken);
        return user is null || user.Status != UserStatuses.Active
            ? null
            : await BuildCurrentUserAsync(user, cancellationToken);
    }

    public async Task<IReadOnlyList<MenuNode>> GetMenuTreeAsync(int userId, CancellationToken cancellationToken = default)
    {
        var menus = await users.GetVisibleMenusAsync(userId, cancellationToken);
        var visible = menus.ToDictionary(menu => menu.Id);
        var roots = new List<MenuNode>();
        foreach (var menu in menus)
        {
            if (menu.ParentId is null)
                roots.Add(menu);
            else if (visible.TryGetValue(menu.ParentId.Value, out var parent))
                parent.Children.Add(menu);
            // Otherwise the parent is hidden, so the submenu is hidden too.
        }

        return PruneEmptyGroups(roots);
    }

    public async Task<AuthResponse?> ChangePasswordAsync(
        int userId, ChangePasswordRequest request, string? clientIp, CancellationToken cancellationToken = default)
    {
        PasswordPolicy.Validate(request.NewPassword);
        var user = await users.GetCredentialsByIdAsync(userId, cancellationToken);
        if (user is null || user.Status != UserStatuses.Active ||
            !passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            return null;

        await users.UpdatePasswordHashAsync(userId, passwordHasher.Hash(request.NewPassword), cancellationToken);

        // Sign out every other session; this one continues with a fresh token pair.
        await tokens.RevokeAllRefreshTokensAsync(userId, cancellationToken);
        return await StartSessionAsync(user, clientIp, cancellationToken);
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        // The caller answers the same way whether or not the account exists.
        var user = await users.GetCredentialsByEmailAsync(email.Trim(), cancellationToken);
        if (user is null || user.Status != UserStatuses.Active)
            return;

        var resetToken = OpaqueToken.Create();
        await tokens.CreatePasswordResetTokenAsync(
            user.Id, OpaqueToken.Hash(resetToken), settings.PasswordResetTokenLifetime, cancellationToken);
        await passwordResetNotifier.SendAsync(user, resetToken, settings.PasswordResetTokenLifetime, cancellationToken);
    }

    public async Task<bool> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        PasswordPolicy.Validate(request.NewPassword);
        var userId = await tokens.ResetPasswordAsync(
            OpaqueToken.Hash(request.Token.Trim()), passwordHasher.Hash(request.NewPassword), cancellationToken);
        return userId is not null;
    }

    private async Task<AuthResponse> StartSessionAsync(
        SecurityUserCredentials user, string? clientIp, CancellationToken cancellationToken)
    {
        var refreshToken = OpaqueToken.Create();
        var refreshExpiresAt = await tokens.CreateRefreshTokenAsync(
            user.Id, OpaqueToken.Hash(refreshToken), settings.RefreshTokenLifetime, TrimIp(clientIp), cancellationToken);
        return await BuildResponseAsync(user, refreshToken, refreshExpiresAt, cancellationToken);
    }

    private async Task<AuthResponse> BuildResponseAsync(
        SecurityUserCredentials user, string refreshToken, DateTime refreshExpiresAt,
        CancellationToken cancellationToken)
    {
        var currentUser = await BuildCurrentUserAsync(user, cancellationToken);
        var accessToken = accessTokenIssuer.Issue(currentUser);
        return new AuthResponse
        {
            AccessToken = accessToken.Token,
            AccessTokenExpiresAt = accessToken.ExpiresAt,
            RefreshToken = refreshToken,
            RefreshTokenExpiresAt = refreshExpiresAt,
            User = currentUser
        };
    }

    private async Task<CurrentUser> BuildCurrentUserAsync(
        SecurityUserCredentials user, CancellationToken cancellationToken)
    {
        var access = await users.GetAccessAsync(user.Id, cancellationToken);
        return new CurrentUser
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            PhotoUrl = user.PhotoUrl,
            Roles = access.Roles,
            Permissions = access.Permissions
        };
    }

    // Drops "#" group menus that end up with no visible children.
    private static List<MenuNode> PruneEmptyGroups(List<MenuNode> menus)
    {
        foreach (var menu in menus)
            menu.Children = PruneEmptyGroups(menu.Children);

        return menus.Where(menu => menu.Children.Count > 0 || menu.Path != "#").ToList();
    }

    private static string? TrimIp(string? clientIp)
        => clientIp is { Length: > MaxIpLength } ? clientIp[..MaxIpLength] : clientIp;
}
