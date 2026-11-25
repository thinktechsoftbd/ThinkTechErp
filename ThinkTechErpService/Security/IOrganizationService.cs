using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpService.Security;

public interface IOrganizationService
{
    Task<IReadOnlyList<Organization>> GetOrganizationsAsync(CancellationToken cancellationToken = default);
    Task<Organization?> GetOrganizationAsync(int organizationId, CancellationToken cancellationToken = default);
    Task<Organization> CreateOrganizationAsync(OrganizationUpsertRequest request, int actingUserId, CancellationToken cancellationToken = default);
    Task<Organization?> UpdateOrganizationAsync(int organizationId, OrganizationUpsertRequest request, int actingUserId, CancellationToken cancellationToken = default);

    /// <summary>The SMS providers an organization can send through (its SMS provider picker), active first.</summary>
    Task<IReadOnlyList<OrganizationSmsProviderOption>> GetSmsProviderOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates an integration client's organization by its Organization Id. The client
    /// may only change organizations created under its own acting user, and never sets Active,
    /// External or the SMS provider: a new one is active and external with no provider, an existing
    /// one keeps them.
    /// </summary>
    Task<ClientOrganizationSync> SyncClientOrganizationAsync(Guid objectId, OrganizationDetails details, int clientUserId, CancellationToken cancellationToken = default);
}
