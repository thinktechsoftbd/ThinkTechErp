using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpRepository.Security;

public interface ISecurityRoleRepository
{
    Task<IReadOnlyList<SecurityRole>> GetRolesAsync(CancellationToken cancellationToken = default);
    Task<SecurityRole?> GetRoleAsync(int roleId, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(string name, int? excludeRoleId, CancellationToken cancellationToken = default);
    Task<int> CreateRoleAsync(RoleUpsertRequest request, CancellationToken cancellationToken = default);
    Task<bool> UpdateRoleAsync(int roleId, RoleUpsertRequest request, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteRoleAsync(int roleId, CancellationToken cancellationToken = default);
}
