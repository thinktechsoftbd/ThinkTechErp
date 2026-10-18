using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpRepository.Security;

public interface IOrganizationRepository
{
    Task<IReadOnlyList<Organization>> GetOrganizationsAsync(CancellationToken cancellationToken = default);
    Task<Organization?> GetOrganizationAsync(int organizationId, CancellationToken cancellationToken = default);
    Task<int> CreateOrganizationAsync(OrganizationUpsertRequest request, int createdBy, CancellationToken cancellationToken = default);
    Task<bool> UpdateOrganizationAsync(int organizationId, OrganizationUpsertRequest request, int updatedBy, CancellationToken cancellationToken = default);
}
