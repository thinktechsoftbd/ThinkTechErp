using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpService.Security;

public interface ISecurityPermissionService
{
    Task<IReadOnlyList<SecurityPermission>> GetPermissionsAsync(CancellationToken cancellationToken = default);
    Task<SecurityPermission?> GetPermissionAsync(int permissionId, CancellationToken cancellationToken = default);
    Task<SecurityPermission> CreatePermissionAsync(PermissionUpsertRequest request, CancellationToken cancellationToken = default);
    Task<SecurityPermission?> UpdatePermissionAsync(int permissionId, PermissionUpsertRequest request, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeletePermissionAsync(int permissionId, CancellationToken cancellationToken = default);
}
