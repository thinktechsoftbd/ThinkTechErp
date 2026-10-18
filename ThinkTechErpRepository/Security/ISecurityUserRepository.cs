using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpRepository.Security;

public interface ISecurityUserRepository
{
    Task<SecurityUserCredentials?> GetCredentialsByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<SecurityUserCredentials?> GetCredentialsByIdAsync(int userId, CancellationToken cancellationToken = default);
    Task<UserAccess> GetAccessAsync(int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MenuNode>> GetVisibleMenusAsync(int userId, CancellationToken cancellationToken = default);
    Task<bool> UpdatePasswordHashAsync(int userId, string passwordHash, CancellationToken cancellationToken = default);

    Task<PagedResult<SecurityUser>> GetUsersAsync(string? search, string? status, int limit, int offset, CancellationToken cancellationToken = default);
    Task<SecurityUser?> GetUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<bool> EmailExistsAsync(string email, int? excludeUserId, CancellationToken cancellationToken = default);
    Task<int> CreateUserAsync(UserCreateRequest request, string passwordHash, CancellationToken cancellationToken = default);
    Task<bool> UpdateUserAsync(int userId, UserUpdateRequest request, CancellationToken cancellationToken = default);
    Task<UserPermissionSet?> GetUserPermissionsAsync(int userId, CancellationToken cancellationToken = default);
    Task<bool> SetUserPermissionsAsync(int userId, int[] permissionIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserMenuAccess>?> GetUserMenusAsync(int userId, CancellationToken cancellationToken = default);
    Task<bool> SetUserMenusAsync(int userId, UserMenuAccessItem[] menus, CancellationToken cancellationToken = default);
}
