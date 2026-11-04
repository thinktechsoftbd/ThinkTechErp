using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpService.Security;

namespace ThinkTechErpAPI.Areas.Security.Controller;

[ApiController]
[Route("api/security/subscription-modules")]
[Authorize(Policy = SecurityPolicies.View)]
[SecurityExceptionFilter]
public sealed class SecuritySubscriptionModulesController(ISubscriptionModuleService moduleService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<SubscriptionModule>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SubscriptionModule>>> GetModules(
        [FromQuery, StringLength(100)] string? search,
        [FromQuery] bool? isActive,
        [FromQuery, Range(1, 500)] int limit = 50,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await moduleService.GetModulesAsync(search, isActive, limit, offset, cancellationToken));

    [HttpGet("{subscriptionModuleId:int}")]
    [ProducesResponseType(typeof(SubscriptionModule), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SubscriptionModule>> GetModule(int subscriptionModuleId, CancellationToken cancellationToken)
    {
        var module = await moduleService.GetModuleAsync(subscriptionModuleId, cancellationToken);
        return module is null ? NotFound() : Ok(module);
    }

    [HttpPost]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(typeof(SubscriptionModule), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SubscriptionModule>> CreateModule(
        [FromBody] SubscriptionModuleUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var module = await moduleService.CreateModuleAsync(request, userId, cancellationToken);
        return CreatedAtAction(nameof(GetModule), new { subscriptionModuleId = module.Id }, module);
    }

    [HttpPut("{subscriptionModuleId:int}")]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(typeof(SubscriptionModule), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SubscriptionModule>> UpdateModule(
        int subscriptionModuleId,
        [FromBody] SubscriptionModuleUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var module = await moduleService.UpdateModuleAsync(subscriptionModuleId, request, userId, cancellationToken);
        return module is null ? NotFound() : Ok(module);
    }

    [HttpDelete("{subscriptionModuleId:int}")]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteModule(int subscriptionModuleId, CancellationToken cancellationToken)
        => await moduleService.DeleteModuleAsync(subscriptionModuleId, cancellationToken) switch
        {
            DeleteOutcome.Deleted => NoContent(),
            DeleteOutcome.NotFound => NotFound(),
            _ => Conflict(new { error = "Organizations subscribe to this module. Deactivate it instead." })
        };
}

/// <summary>
/// For integration clients with the thinktech-website.exchange scope (the ThinkTech website): the
/// active subscription modules, for its pricing page. Takes an integration token from
/// POST api/security/integration/token; user tokens are rejected here.
/// </summary>
[ApiController]
[Route("api/security/integration/subscription-modules")]
[Authorize(Policy = IntegrationPolicies.WebsiteExchange)]
[EnableRateLimiting(IntegrationPolicies.RateLimit)]
[SecurityExceptionFilter]
public sealed class SecuritySubscriptionModulesIntegrationController(
    ISubscriptionModuleService moduleService,
    IntegrationClientStore clients) : ControllerBase
{
    // The catalogue is small; this is the most GetModulesAsync returns in one page.
    private const int MaxModules = 500;

    [HttpGet]
    [ProducesResponseType(typeof(PublicSubscriptionModule[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PublicSubscriptionModule[]>> GetActiveModules(CancellationToken cancellationToken)
    {
        // Looked up again so a client removed from configuration loses its unexpired tokens too.
        var client = await clients.FindAsync(User.FindFirst(IntegrationPolicies.ClientIdClaim)?.Value, cancellationToken);
        if (client is null || !client.Scopes.Contains(IntegrationScopes.WebsiteExchange))
            return Unauthorized();

        var modules = await moduleService.GetModulesAsync(null, true, MaxModules, 0, cancellationToken);
        return Ok(modules.Items
            .Select(module => new PublicSubscriptionModule
            {
                ObjectId = module.ObjectId,
                ModuleName = module.ModuleName,
                Remarks = module.Remarks
            })
            .ToArray());
    }
}
