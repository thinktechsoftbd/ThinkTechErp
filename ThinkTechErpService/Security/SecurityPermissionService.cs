using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpService.Security;

public sealed class SecurityPermissionService(ISecurityPermissionRepository permissions) : ISecurityPermissionService
{
    public Task<IReadOnlyList<SecurityPermission>> GetPermissionsAsync(CancellationToken cancellationToken = default)
        => permissions.GetPermissionsAsync(cancellationToken);

    public Task<SecurityPermission?> GetPermissionAsync(int permissionId, CancellationToken cancellationToken = default)
        => permissions.GetPermissionAsync(permissionId, cancellationToken);

    public async Task<SecurityPermission> CreatePermissionAsync(
        PermissionUpsertRequest request, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        var permissionId = await SecurityDbErrors.ExecuteAsync(() =>
            permissions.CreatePermissionAsync(request, cancellationToken));
        return await permissions.GetPermissionAsync(permissionId, cancellationToken)
            ?? throw new InvalidOperationException("Created permission could not be read back.");
    }

    public async Task<SecurityPermission?> UpdatePermissionAsync(
        int permissionId, PermissionUpsertRequest request, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        if (!await SecurityDbErrors.ExecuteAsync(() =>
                permissions.UpdatePermissionAsync(permissionId, request, cancellationToken)))
            return null;

        return await permissions.GetPermissionAsync(permissionId, cancellationToken);
    }

    public Task<DeleteOutcome> DeletePermissionAsync(int permissionId, CancellationToken cancellationToken = default)
        => permissions.DeletePermissionAsync(permissionId, cancellationToken);

    private static void Normalize(PermissionUpsertRequest request)
    {
        request.Name = request.Name.Trim();
        request.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
    }
}
