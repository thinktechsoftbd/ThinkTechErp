using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpService.Security;

public sealed class SecurityUserService(
    ISecurityUserRepository users,
    IAuthTokenRepository tokens,
    IPasswordHasher passwordHasher) : ISecurityUserService
{
    public Task<PagedResult<SecurityUser>> GetUsersAsync(
        string? search, string? status, int limit, int offset, CancellationToken cancellationToken = default)
        => users.GetUsersAsync(
            string.IsNullOrWhiteSpace(search) ? null : search.Trim(), status, limit, offset, cancellationToken);

    public Task<SecurityUser?> GetUserAsync(int userId, CancellationToken cancellationToken = default)
        => users.GetUserAsync(userId, cancellationToken);

    public async Task<SecurityUser> CreateUserAsync(UserCreateRequest request, CancellationToken cancellationToken = default)
    {
        PasswordPolicy.Validate(request.Password);
        request.Name = request.Name.Trim();
        request.Email = request.Email.Trim();
        if (await users.EmailExistsAsync(request.Email, null, cancellationToken))
            throw new SecurityConflictException("A user with this email already exists.");

        var userId = await SecurityDbErrors.ExecuteAsync(() =>
            users.CreateUserAsync(request, passwordHasher.Hash(request.Password), cancellationToken));
        return await users.GetUserAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("Created user could not be read back.");
    }

    public async Task<SecurityUser?> UpdateUserAsync(
        int userId, UserUpdateRequest request, int actingUserId, CancellationToken cancellationToken = default)
    {
        if (userId == actingUserId && request.Status != UserStatuses.Active)
            throw new SecurityValidationException("You cannot deactivate your own account.");

        request.Name = request.Name.Trim();
        request.Email = request.Email.Trim();
        if (await users.EmailExistsAsync(request.Email, userId, cancellationToken))
            throw new SecurityConflictException("A user with this email already exists.");

        if (!await SecurityDbErrors.ExecuteAsync(() => users.UpdateUserAsync(userId, request, cancellationToken)))
            return null;

        // A deactivated user can't refresh any more; their access token lapses within minutes.
        // The user is already saved as inactive, so finish the revocation even if the caller hangs up.
        if (request.Status == UserStatuses.Inactive)
            await tokens.RevokeAllRefreshTokensAsync(userId, CancellationToken.None);

        return await users.GetUserAsync(userId, cancellationToken);
    }

    public async Task<bool> SetPasswordAsync(int userId, string newPassword, CancellationToken cancellationToken = default)
    {
        PasswordPolicy.Validate(newPassword);
        if (!await users.UpdatePasswordHashAsync(userId, passwordHasher.Hash(newPassword), cancellationToken))
            return false;

        // The new password is saved; old sessions must end even if the caller hangs up.
        await tokens.RevokeAllRefreshTokensAsync(userId, CancellationToken.None);
        return true;
    }

    public Task<UserPermissionSet?> GetPermissionsAsync(int userId, CancellationToken cancellationToken = default)
        => users.GetUserPermissionsAsync(userId, cancellationToken);

    public async Task<UserPermissionSet?> SetPermissionsAsync(
        int userId, int[] permissionIds, CancellationToken cancellationToken = default)
    {
        if (!await SecurityDbErrors.ExecuteAsync(() =>
                users.SetUserPermissionsAsync(userId, permissionIds, cancellationToken)))
            return null;

        return await users.GetUserPermissionsAsync(userId, cancellationToken);
    }

    public Task<IReadOnlyList<UserMenuAccess>?> GetMenusAsync(int userId, CancellationToken cancellationToken = default)
        => users.GetUserMenusAsync(userId, cancellationToken);

    public async Task<IReadOnlyList<UserMenuAccess>?> SetMenusAsync(
        int userId, UserMenuAccessItem[] menus, CancellationToken cancellationToken = default)
    {
        if (menus.Select(menu => menu.MenuId).Distinct().Count() != menus.Length)
            throw new SecurityValidationException("Each menu can appear only once.");

        if (!await SecurityDbErrors.ExecuteAsync(() => users.SetUserMenusAsync(userId, menus, cancellationToken)))
            return null;

        return await users.GetUserMenusAsync(userId, cancellationToken);
    }
}
