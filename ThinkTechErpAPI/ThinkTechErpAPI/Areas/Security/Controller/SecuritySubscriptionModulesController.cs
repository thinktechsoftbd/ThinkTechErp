using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
