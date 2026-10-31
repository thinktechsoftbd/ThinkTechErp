using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpService.Security;

public interface IOrganizationService
{
    Task<IReadOnlyList<Organization>> GetOrganizationsAsync(CancellationToken cancellationToken = default);
    Task<Organization?> GetOrganizationAsync(int organizationId, CancellationToken cancellationToken = default);
    Task<Organization> CreateOrganizationAsync(OrganizationUpsertRequest request, int actingUserId, CancellationToken cancellationToken = default);
    Task<Organization?> UpdateOrganizationAsync(int organizationId, OrganizationUpsertRequest request, int actingUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates an integration client's organization by its Organization Id. The client
    /// may only change organizations created under its own acting user, and never sets Active or
    /// External: a new one is active and external, an existing one keeps its flags.
    /// </summary>
    Task<ClientOrganizationSync> SyncClientOrganizationAsync(Guid objectId, OrganizationDetails details, int clientUserId, CancellationToken cancellationToken = default);
}
