using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpRepository.Security;

public interface IOrganizationRepository
{
    Task<IReadOnlyList<Organization>> GetOrganizationsAsync(CancellationToken cancellationToken = default);
    Task<Organization?> GetOrganizationAsync(int organizationId, CancellationToken cancellationToken = default);
    Task<Organization?> GetOrganizationByObjectIdAsync(Guid objectId, CancellationToken cancellationToken = default);
    /// <summary>Refused (BusinessRuleException) when the SMS provider doesn't exist or is inactive.</summary>
    Task<int> CreateOrganizationAsync(OrganizationUpsertRequest request, int createdBy, CancellationToken cancellationToken = default);
    /// <summary>
    /// A changed SMS provider must exist and be active, and have the organization's allocated SMS left to allocate
    /// (they move to its stock); an organization with SMS allocated can't be left without one.
    /// </summary>
    Task<bool> UpdateOrganizationAsync(int organizationId, OrganizationUpsertRequest request, int updatedBy, CancellationToken cancellationToken = default);
    /// <summary>Every SMS provider, active first, for an organization's SMS provider picker.</summary>
    Task<IReadOnlyList<OrganizationSmsProviderOption>> GetSmsProviderOptionsAsync(CancellationToken cancellationToken = default);
}
