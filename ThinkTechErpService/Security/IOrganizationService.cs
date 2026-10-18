using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpService.Security;

public interface IOrganizationService
{
    Task<IReadOnlyList<Organization>> GetOrganizationsAsync(CancellationToken cancellationToken = default);
    Task<Organization?> GetOrganizationAsync(int organizationId, CancellationToken cancellationToken = default);
    Task<Organization> CreateOrganizationAsync(OrganizationUpsertRequest request, int actingUserId, CancellationToken cancellationToken = default);
    Task<Organization?> UpdateOrganizationAsync(int organizationId, OrganizationUpsertRequest request, int actingUserId, CancellationToken cancellationToken = default);
}
