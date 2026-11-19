using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpService.Security;

public interface ISecurityPermissionService
{
    Task<IReadOnlyList<SecurityPermission>> GetPermissionsAsync(CancellationToken cancellationToken = default);

    /// <summary>One page of the permissions whose code or name contains `search`, sorted by code, name or description.</summary>
    Task<PagedResult<SecurityPermission>> SearchPermissionsAsync(string? search, string sort, bool descending, int limit, int offset, CancellationToken cancellationToken = default);
    Task<SecurityPermission?> GetPermissionAsync(int permissionId, CancellationToken cancellationToken = default);
    Task<SecurityPermission> CreatePermissionAsync(PermissionUpsertRequest request, CancellationToken cancellationToken = default);
    Task<SecurityPermission?> UpdatePermissionAsync(int permissionId, PermissionUpsertRequest request, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeletePermissionAsync(int permissionId, CancellationToken cancellationToken = default);
}
