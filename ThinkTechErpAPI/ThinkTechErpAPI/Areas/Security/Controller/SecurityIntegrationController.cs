using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpService.Security;

namespace ThinkTechErpAPI.Areas.Security.Controller;

/// <summary>
/// Server-to-server API for client applications such as a MediDesk installation. A client trades
/// its client ID and secret (Authentication:Integration:Clients) for a short-lived access token and
/// calls the other endpoints with it. Those tokens work only here; user tokens don't work here.
/// </summary>
[ApiController]
[Route("api/security/integration")]
[Authorize(Policy = IntegrationPolicies.OrganizationSync)]
[SecurityExceptionFilter]
public sealed class SecurityIntegrationController(
    IntegrationClientStore clients,
    IntegrationTokenIssuer tokenIssuer,
    IOrganizationService organizationService,
    ILogger<SecurityIntegrationController> logger) : ControllerBase
{
    [HttpPost("token")]
    [AllowAnonymous]
    [EnableRateLimiting(SecurityPolicies.AuthRateLimit)]
    [ProducesResponseType(typeof(IntegrationTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<IntegrationTokenResponse> IssueToken([FromBody] IntegrationTokenRequest request)
    {
        var client = clients.Authenticate(request.ClientId, request.ClientSecret);
        if (client is null)
        {
            logger.LogWarning("Rejected integration client credentials for {ClientId} from {ClientIp}.",
                request.ClientId, HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { error = "Invalid client credentials." });
        }

        return Ok(tokenIssuer.Issue(client));
    }

    /// <summary>
    /// Creates (201) or updates (200) the client's organization with this Organization Id
    /// (object_id). 403 when the Organization Id belongs to an organization the client didn't
    /// create. Active and External are left to the ERP.
    /// </summary>
    [HttpPut("organizations/{objectId:guid}")]
    [EnableRateLimiting(IntegrationPolicies.RateLimit)]
    [ProducesResponseType(typeof(Organization), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Organization), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Organization>> SyncOrganization(
        Guid objectId,
        [FromBody] OrganizationDetails request,
        CancellationToken cancellationToken)
    {
        // Looked up again so a client removed from configuration loses its unexpired tokens too.
        var client = clients.Find(User.FindFirst(IntegrationPolicies.ClientIdClaim)?.Value);
        if (client is null || !client.Scopes.Contains(IntegrationScopes.OrganizationSync))
            return Unauthorized();

        var result = await organizationService.SyncClientOrganizationAsync(
            objectId, request, client.ActingUserId, cancellationToken);
        return result.Status switch
        {
            ClientOrganizationSyncStatus.Created => StatusCode(StatusCodes.Status201Created, result.Organization),
            ClientOrganizationSyncStatus.Updated => Ok(result.Organization),
            _ => StatusCode(StatusCodes.Status403Forbidden,
                new { error = "This Organization Id belongs to an organization this client can't change." })
        };
    }
}
