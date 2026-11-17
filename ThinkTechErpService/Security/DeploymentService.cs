using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpService.Security;

public sealed class DeploymentService(IDeploymentRepository deployments) : IDeploymentService
{
    public Task<PagedResult<DeploymentOrganization>> GetOrganizationsAsync(
        string? search, int limit, int offset, CancellationToken cancellationToken = default)
        => deployments.GetOrganizationsAsync(Clean(search), limit, offset, cancellationToken);

    public async Task<OrganizationDeployments?> GetOrganizationDeploymentsAsync(
        Guid organizationObjectId, CancellationToken cancellationToken = default)
    {
        var organization = await deployments.GetOrganizationAsync(organizationObjectId, cancellationToken);
        return organization is null
            ? null
            : new OrganizationDeployments
            {
                Organization = organization,
                Deployments = await deployments.GetDeploymentsAsync(organizationObjectId, cancellationToken)
            };
    }

    public Task<DeploymentInfo?> GetDeploymentAsync(int deploymentId, CancellationToken cancellationToken = default)
        => deployments.GetDeploymentAsync(deploymentId, cancellationToken);

    public Task<IReadOnlyList<DeploymentServiceType>> GetServiceTypesAsync(CancellationToken cancellationToken = default)
        => deployments.GetServiceTypesAsync(cancellationToken);

    public async Task<DeploymentInfo?> CreateDeploymentAsync(
        Guid organizationObjectId, DeploymentInfoUpsertRequest request, int actingUserId,
        CancellationToken cancellationToken = default)
    {
        if (await deployments.GetOrganizationAsync(organizationObjectId, cancellationToken) is null)
            return null;

        await NormalizeAsync(request, keptServiceTypeId: null, cancellationToken);
        var id = await deployments.CreateDeploymentAsync(organizationObjectId, request, actingUserId, cancellationToken);
        return await deployments.GetDeploymentAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Created deployment could not be read back.");
    }

    public async Task<DeploymentInfo?> UpdateDeploymentAsync(
        int deploymentId, DeploymentInfoUpsertRequest request, int actingUserId, CancellationToken cancellationToken = default)
    {
        var existing = await deployments.GetDeploymentAsync(deploymentId, cancellationToken);
        if (existing is null)
            return null;

        await NormalizeAsync(request, existing.ServiceTypeId, cancellationToken);
        if (!await deployments.UpdateDeploymentAsync(deploymentId, request, actingUserId, cancellationToken))
            return null;

        return await deployments.GetDeploymentAsync(deploymentId, cancellationToken);
    }

    public Task<bool> DeleteDeploymentAsync(int deploymentId, CancellationToken cancellationToken = default)
        => deployments.DeleteDeploymentAsync(deploymentId, cancellationToken);

    /// <summary>
    /// The service type must be on the "Service Type" list and active (a deployment may keep one made inactive
    /// since); remarks and lines are trimmed, and empty lines dropped.
    /// </summary>
    private async Task NormalizeAsync(DeploymentInfoUpsertRequest request, long? keptServiceTypeId, CancellationToken cancellationToken)
    {
        var serviceType = (await deployments.GetServiceTypesAsync(cancellationToken)).FirstOrDefault(type => type.Id == request.ServiceTypeId)
            ?? throw new SecurityValidationException(
                $"Choose a service type from the {DeploymentRules.ServiceTypeLookupTable} lookup list.");
        if (!serviceType.IsActive && serviceType.Id != keptServiceTypeId)
            throw new SecurityValidationException($"The service type {serviceType.Name} is inactive.");

        request.Remarks = Clean(request.Remarks);
        request.Details = request.Details
            .Select(detail => new DeploymentInfoDetailRequest { Description = Clean(detail.Description) })
            .Where(detail => detail.Description is not null)
            .ToList();
        if (request.Details.Count > DeploymentRules.MaxDescriptions)
            throw new SecurityValidationException($"A deployment can have at most {DeploymentRules.MaxDescriptions} detail lines.");
        if (request.Details.Any(detail => detail.Description!.Length > DeploymentRules.DescriptionMaxLength))
            throw new SecurityValidationException(
                $"A detail line can be at most {DeploymentRules.DescriptionMaxLength} characters.");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
