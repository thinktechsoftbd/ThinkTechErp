using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpRepository.Security;

public interface IDeploymentRepository
{
    /// <summary>Every organization (by name, code or slug) with how many deployments it has.</summary>
    Task<PagedResult<DeploymentOrganization>> GetOrganizationsAsync(
        string? search, int limit, int offset, CancellationToken cancellationToken = default);

    Task<DeploymentOrganization?> GetOrganizationAsync(Guid organizationObjectId, CancellationToken cancellationToken = default);

    /// <summary>The organization's deployments with their detail lines, by service type.</summary>
    Task<IReadOnlyList<DeploymentInfo>> GetDeploymentsAsync(Guid organizationObjectId, CancellationToken cancellationToken = default);

    Task<DeploymentInfo?> GetDeploymentAsync(int deploymentId, CancellationToken cancellationToken = default);

    /// <summary>The "Service Type" lookup list, inactive values included.</summary>
    Task<IReadOnlyList<DeploymentServiceType>> GetServiceTypesAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a deployment and its detail lines (already checked and trimmed); returns its id.</summary>
    Task<int> CreateDeploymentAsync(
        Guid organizationObjectId, DeploymentInfoUpsertRequest request, int actingUserId, CancellationToken cancellationToken = default);

    /// <summary>Changes a deployment and replaces its detail lines; false when there is no such deployment.</summary>
    Task<bool> UpdateDeploymentAsync(
        int deploymentId, DeploymentInfoUpsertRequest request, int actingUserId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a deployment with its detail lines; false when there is no such deployment.</summary>
    Task<bool> DeleteDeploymentAsync(int deploymentId, CancellationToken cancellationToken = default);
}
