using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpService.Security;

public interface ISecurityUserService
{
    Task<PagedResult<SecurityUser>> GetUsersAsync(string? search, string? status, int limit, int offset, CancellationToken cancellationToken = default);
    Task<SecurityUser?> GetUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<SecurityUser> CreateUserAsync(UserCreateRequest request, CancellationToken cancellationToken = default);
    Task<SecurityUser?> UpdateUserAsync(int userId, UserUpdateRequest request, int actingUserId, CancellationToken cancellationToken = default);
    Task<bool> SetPasswordAsync(int userId, string newPassword, CancellationToken cancellationToken = default);
    Task<UserPermissionSet?> GetPermissionsAsync(int userId, CancellationToken cancellationToken = default);
    Task<UserPermissionSet?> SetPermissionsAsync(int userId, int[] permissionIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserMenuAccess>?> GetMenusAsync(int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserMenuAccess>?> SetMenusAsync(int userId, UserMenuAccessItem[] menus, CancellationToken cancellationToken = default);
}
