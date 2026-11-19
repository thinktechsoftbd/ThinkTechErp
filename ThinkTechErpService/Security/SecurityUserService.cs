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
        Normalize(request);
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
        if (userId == actingUserId && request.IsLocked)
            throw new SecurityValidationException("You cannot lock your own account.");

        Normalize(request);
        if (await users.EmailExistsAsync(request.Email, userId, cancellationToken))
            throw new SecurityConflictException("A user with this email already exists.");

        if (!await SecurityDbErrors.ExecuteAsync(() => users.UpdateUserAsync(userId, request, cancellationToken)))
            return null;

        // A deactivated or locked user can't refresh any more; their access token lapses within minutes.
        // The user is already saved, so finish the revocation even if the caller hangs up.
        if (request.Status == UserStatuses.Inactive || request.IsLocked)
            await tokens.RevokeAllRefreshTokensAsync(userId, CancellationToken.None);

        return await users.GetUserAsync(userId, cancellationToken);
    }

    public async Task<SecurityUser?> SetStatusAsync(
        int userId, string status, int actingUserId, CancellationToken cancellationToken = default)
    {
        if (userId == actingUserId && status != UserStatuses.Active)
            throw new SecurityValidationException("You cannot deactivate your own account.");

        if (!await users.UpdateStatusAsync(userId, status, cancellationToken))
            return null;

        // Like a profile save: an inactive user's sessions end even if the caller hangs up.
        if (status == UserStatuses.Inactive)
            await tokens.RevokeAllRefreshTokensAsync(userId, CancellationToken.None);

        return await users.GetUserAsync(userId, cancellationToken);
    }

    /// <summary>Trims the fields, empties blank optional ones, and fills the full name from first and last name.</summary>
    private static void Normalize(UserProfileRequest request)
    {
        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        request.Name = request.Name.Trim();
        request.LastName = Clean(request.LastName);
        request.Email = request.Email.Trim();
        request.CellphoneNo = request.CellphoneNo.Trim();
        if (request.CellphoneNo.Length == 0)
            throw new SecurityValidationException("Enter the phone number.");
        request.PhotoUrl = Clean(request.PhotoUrl);
        request.AddressLine1 = Clean(request.AddressLine1);
        request.AddressLine2 = Clean(request.AddressLine2);
        request.ShortBio = Clean(request.ShortBio);
        request.FullName = Clean(request.FullName) ?? string.Join(' ', new[] { request.Name, request.LastName }.Where(part => part is not null));
        if (request.FullName.Length > 300)
            request.FullName = request.FullName[..300];
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
