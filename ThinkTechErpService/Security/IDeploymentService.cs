using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpService.Security;

public interface IDeploymentService
{
    /// <summary>Every organization (by name, code or slug) with how many deployments it has.</summary>
    Task<PagedResult<DeploymentOrganization>> GetOrganizationsAsync(
        string? search, int limit, int offset, CancellationToken cancellationToken = default);

    /// <summary>The organization and its deployments; null when there is no such organization.</summary>
    Task<OrganizationDeployments?> GetOrganizationDeploymentsAsync(Guid organizationObjectId, CancellationToken cancellationToken = default);

    Task<DeploymentInfo?> GetDeploymentAsync(int deploymentId, CancellationToken cancellationToken = default);

    /// <summary>The "Service Type" lookup list, inactive values included.</summary>
    Task<IReadOnlyList<DeploymentServiceType>> GetServiceTypesAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a deployment for the organization; null when there is no such organization.</summary>
    /// <exception cref="SecurityValidationException">The service type isn't on the list, or a line is too long.</exception>
    Task<DeploymentInfo?> CreateDeploymentAsync(
        Guid organizationObjectId, DeploymentInfoUpsertRequest request, int actingUserId, CancellationToken cancellationToken = default);

    /// <summary>Changes a deployment and replaces its detail lines; null when there is no such deployment.</summary>
    /// <exception cref="SecurityValidationException">The service type isn't on the list, or a line is too long.</exception>
    Task<DeploymentInfo?> UpdateDeploymentAsync(
        int deploymentId, DeploymentInfoUpsertRequest request, int actingUserId, CancellationToken cancellationToken = default);

    Task<bool> DeleteDeploymentAsync(int deploymentId, CancellationToken cancellationToken = default);
}
