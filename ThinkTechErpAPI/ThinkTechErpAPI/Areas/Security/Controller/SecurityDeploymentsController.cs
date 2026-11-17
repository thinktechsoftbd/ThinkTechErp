using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpService.Security;

namespace ThinkTechErpAPI.Areas.Security.Controller;

/// <summary>
/// What is deployed for each organization (public.deployment_info_h, by service type) and its detail lines
/// (public.deployment_info_d). Organizations are addressed by their object id.
/// </summary>
[ApiController]
[Route("api/security/deployments")]
[Authorize(Policy = DeploymentPolicies.View)]
[SecurityExceptionFilter]
public sealed class SecurityDeploymentsController(IDeploymentService deploymentService) : ControllerBase
{
    /// <summary>Every organization (search by name, code or slug) with how many deployments it has.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<DeploymentOrganization>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DeploymentOrganization>>> GetOrganizations(
        [FromQuery, StringLength(100)] string? search,
        [FromQuery, Range(1, 500)] int limit = 50,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await deploymentService.GetOrganizationsAsync(search, limit, offset, cancellationToken));

    /// <summary>The "Service Type" lookup list, inactive values included.</summary>
    [HttpGet("service-types")]
    [ProducesResponseType(typeof(IReadOnlyList<DeploymentServiceType>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DeploymentServiceType>>> GetServiceTypes(CancellationToken cancellationToken)
        => Ok(await deploymentService.GetServiceTypesAsync(cancellationToken));

    /// <summary>An organization and its deployments.</summary>
    [HttpGet("organizations/{organizationObjectId:guid}")]
    [ProducesResponseType(typeof(OrganizationDeployments), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrganizationDeployments>> GetOrganizationDeployments(
        Guid organizationObjectId, CancellationToken cancellationToken)
    {
        var result = await deploymentService.GetOrganizationDeploymentsAsync(organizationObjectId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Adds a deployment for the organization.</summary>
    [HttpPost("organizations/{organizationObjectId:guid}")]
    [Authorize(Policy = DeploymentPolicies.Manage)]
    [ProducesResponseType(typeof(DeploymentInfo), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeploymentInfo>> CreateDeployment(
        Guid organizationObjectId,
        [FromBody] DeploymentInfoUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var deployment = await deploymentService.CreateDeploymentAsync(organizationObjectId, request, userId, cancellationToken);
        return deployment is null
            ? NotFound()
            : CreatedAtAction(nameof(GetDeployment), new { deploymentId = deployment.Id }, deployment);
    }

    [HttpGet("{deploymentId:int:min(1)}")]
    [ProducesResponseType(typeof(DeploymentInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeploymentInfo>> GetDeployment(int deploymentId, CancellationToken cancellationToken)
    {
        var deployment = await deploymentService.GetDeploymentAsync(deploymentId, cancellationToken);
        return deployment is null ? NotFound() : Ok(deployment);
    }

    /// <summary>Changes a deployment; its detail lines are replaced by the ones sent.</summary>
    [HttpPut("{deploymentId:int:min(1)}")]
    [Authorize(Policy = DeploymentPolicies.Manage)]
    [ProducesResponseType(typeof(DeploymentInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeploymentInfo>> UpdateDeployment(
        int deploymentId,
        [FromBody] DeploymentInfoUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var deployment = await deploymentService.UpdateDeploymentAsync(deploymentId, request, userId, cancellationToken);
        return deployment is null ? NotFound() : Ok(deployment);
    }

    /// <summary>Deletes a deployment with its detail lines.</summary>
    [HttpDelete("{deploymentId:int:min(1)}")]
    [Authorize(Policy = DeploymentPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDeployment(int deploymentId, CancellationToken cancellationToken)
        => await deploymentService.DeleteDeploymentAsync(deploymentId, cancellationToken) ? NoContent() : NotFound();
}
