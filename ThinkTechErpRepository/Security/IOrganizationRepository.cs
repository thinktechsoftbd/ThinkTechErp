using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Storage;

namespace ThinkTechErpRepository.Security;

public interface IOrganizationRepository
{
    Task<IReadOnlyList<Organization>> GetOrganizationsAsync(CancellationToken cancellationToken = default);
    Task<Organization?> GetOrganizationAsync(int organizationId, CancellationToken cancellationToken = default);
    Task<Organization?> GetOrganizationByObjectIdAsync(Guid objectId, CancellationToken cancellationToken = default);
    Task<int> CreateOrganizationAsync(OrganizationUpsertRequest request, int createdBy, CancellationToken cancellationToken = default);
    Task<bool> UpdateOrganizationAsync(int organizationId, OrganizationUpsertRequest request, int updatedBy, CancellationToken cancellationToken = default);

    /// <summary>The organization's English name and slug (null when it has none), or null when there's no such organization.</summary>
    Task<OrganizationSlug?> GetOrganizationSlugAsync(int organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives the organization <paramref name="slug"/> when it has none, or "{slug}-{id}" when another organization
    /// has that one, and returns the organization's slug. An existing slug is never changed.
    /// </summary>
    Task<string?> EnsureOrganizationSlugAsync(int organizationId, string slug, CancellationToken cancellationToken = default);
}
