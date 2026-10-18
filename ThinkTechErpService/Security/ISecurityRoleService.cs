using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpService.Security;

public interface ISecurityRoleService
{
    Task<IReadOnlyList<SecurityRole>> GetRolesAsync(CancellationToken cancellationToken = default);
    Task<SecurityRole?> GetRoleAsync(int roleId, CancellationToken cancellationToken = default);
    Task<SecurityRole> CreateRoleAsync(RoleUpsertRequest request, CancellationToken cancellationToken = default);
    Task<SecurityRole?> UpdateRoleAsync(int roleId, RoleUpsertRequest request, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteRoleAsync(int roleId, CancellationToken cancellationToken = default);
}
