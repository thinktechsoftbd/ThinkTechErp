using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpService.Security;

namespace ThinkTechErpAPI.Areas.Security.Controller;

/// <summary>Subscription plans with their features and each feature's details (SECURITY.V to read, SECURITY.M to change).</summary>
[ApiController]
[Route("api/security/subscription-plans")]
[Authorize(Policy = SecurityPolicies.View)]
[SecurityExceptionFilter]
public sealed class SecuritySubscriptionPlansController(ISubscriptionPlanService planService) : ControllerBase
{
    /// <summary>A page of plans by name or short note, each with its features and details.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<SubscriptionPlan>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SubscriptionPlan>>> GetPlans(
        [FromQuery, StringLength(100)] string? search,
        [FromQuery] bool? isActive,
        [FromQuery, Range(1, 500)] int limit = 50,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await planService.GetPlansAsync(search, isActive, limit, offset, cancellationToken));

    [HttpGet("{subscriptionPlanId:int}")]
    [ProducesResponseType(typeof(SubscriptionPlan), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SubscriptionPlan>> GetPlan(int subscriptionPlanId, CancellationToken cancellationToken)
    {
        var plan = await planService.GetPlanAsync(subscriptionPlanId, cancellationToken);
        return plan is null ? NotFound() : Ok(plan);
    }

    /// <summary>A plan with its features and their details.</summary>
    [HttpPost]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(typeof(SubscriptionPlan), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SubscriptionPlan>> CreatePlan(
        [FromBody] SubscriptionPlanUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var plan = await planService.CreatePlanAsync(request, userId, cancellationToken);
        return CreatedAtAction(nameof(GetPlan), new { subscriptionPlanId = plan.Id }, plan);
    }

    /// <summary>The plan and its features: listed ones are kept or added, the others removed.</summary>
    [HttpPut("{subscriptionPlanId:int}")]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(typeof(SubscriptionPlan), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SubscriptionPlan>> UpdatePlan(
        int subscriptionPlanId,
        [FromBody] SubscriptionPlanUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var plan = await planService.UpdatePlanAsync(subscriptionPlanId, request, userId, cancellationToken);
        return plan is null ? NotFound() : Ok(plan);
    }

    /// <summary>The plan with its features and their details.</summary>
    [HttpDelete("{subscriptionPlanId:int}")]
    [Authorize(Policy = SecurityPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeletePlan(int subscriptionPlanId, CancellationToken cancellationToken)
        => await planService.DeletePlanAsync(subscriptionPlanId, cancellationToken) switch
        {
            DeleteOutcome.Deleted => NoContent(),
            DeleteOutcome.NotFound => NotFound(),
            _ => Conflict(new { error = "The plan is in use. Deactivate it instead." })
        };
}
