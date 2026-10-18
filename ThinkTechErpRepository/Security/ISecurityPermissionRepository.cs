using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpRepository.Security;

public interface ISecurityPermissionRepository
{
    Task<IReadOnlyList<SecurityPermission>> GetPermissionsAsync(CancellationToken cancellationToken = default);
    Task<SecurityPermission?> GetPermissionAsync(int permissionId, CancellationToken cancellationToken = default);
    Task<int> CreatePermissionAsync(PermissionUpsertRequest request, CancellationToken cancellationToken = default);
    Task<bool> UpdatePermissionAsync(int permissionId, PermissionUpsertRequest request, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeletePermissionAsync(int permissionId, CancellationToken cancellationToken = default);
}
