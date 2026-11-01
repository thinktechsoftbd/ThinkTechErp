using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Sms;
using ThinkTechErpService.Sms;

namespace ThinkTechErpAPI.Areas.SMS.Controller;

/// <summary>Query string of the purchase and allocation lists.</summary>
public sealed class SmsListQueryString
{
    [StringLength(100)] public string? Search { get; set; }
    public int? ProviderId { get; set; }
    public Guid? OrganizationId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    [Range(1, 500)] public int Limit { get; set; } = 20;
    [Range(0, int.MaxValue)] public int Offset { get; set; }

    public SmsListQuery ToQuery() => new()
    {
        Search = Search,
        ProviderId = ProviderId,
        OrganizationObjectId = OrganizationId,
        From = From,
        To = To,
        Limit = Limit,
        Offset = Offset
    };
}

/// <summary>Bought, allocated and used SMS at a glance, per provider and for the organizations lowest on SMS.</summary>
[Route("api/sms/overview")]
[Authorize(Policy = SmsPolicies.View)]
public sealed class SmsOverviewController(ISmsService sms) : ErpControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SmsOverview>> GetOverview(CancellationToken cancellationToken)
        => Ok(await sms.GetOverviewAsync(cancellationToken));
}

/// <summary>SMS gateways. The API password and key are write-only: responses only say whether they are set.</summary>
[Route("api/sms/providers")]
[Authorize(Policy = SmsPolicies.View)]
public sealed class SmsProvidersController(ISmsService sms) : ErpControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SmsProvider>>> GetProviders([FromQuery] bool? isActive, CancellationToken cancellationToken)
        => Ok(await sms.GetProvidersAsync(isActive, cancellationToken));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SmsProvider>> GetProvider(int id, CancellationToken cancellationToken)
        => OkOrNotFound(await sms.GetProviderAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = SmsPolicies.Manage)]
    public async Task<ActionResult<SmsProvider>> CreateProvider([FromBody] SmsProviderUpsertRequest request, CancellationToken cancellationToken)
    {
        var provider = await sms.CreateProviderAsync(request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetProvider), new { id = provider.Id }, provider);
    }

    /// <summary>ApiPassword / ApiKey: leave out (null) to keep, send "" to remove.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = SmsPolicies.Manage)]
    public async Task<ActionResult<SmsProvider>> UpdateProvider(int id, [FromBody] SmsProviderUpsertRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await sms.UpdateProviderAsync(id, request, UserId, cancellationToken));

    [HttpDelete("{id:int}")]
    [Authorize(Policy = SmsPolicies.Manage)]
    public async Task<IActionResult> DeleteProvider(int id, CancellationToken cancellationToken)
        => DeleteResult(await sms.DeleteProviderAsync(id, cancellationToken),
            "SMS were bought from or allocated with this provider. Deactivate it instead.");
}

/// <summary>SMS bought from a provider, with the cost in its currency and in BDT.</summary>
[Route("api/sms/purchases")]
[Authorize(Policy = SmsPolicies.View)]
public sealed class SmsPurchasesController(ISmsService sms) : ErpControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<SmsPurchase>>> GetPurchases([FromQuery] SmsListQueryString query, CancellationToken cancellationToken)
        => Ok(await sms.GetPurchasesAsync(query.ToQuery(), cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<SmsPurchase>> GetPurchase(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await sms.GetPurchaseAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = SmsPolicies.Manage)]
    public async Task<ActionResult<SmsPurchase>> CreatePurchase([FromBody] SmsPurchaseUpsertRequest request, CancellationToken cancellationToken)
    {
        var purchase = await sms.CreatePurchaseAsync(request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetPurchase), new { id = purchase.Id }, purchase);
    }

    /// <summary>Refused when the provider would be left with fewer SMS bought than allocated.</summary>
    [HttpPut("{id:long}")]
    [Authorize(Policy = SmsPolicies.Manage)]
    public async Task<ActionResult<SmsPurchase>> UpdatePurchase(long id, [FromBody] SmsPurchaseUpsertRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await sms.UpdatePurchaseAsync(id, request, UserId, cancellationToken));

    [HttpDelete("{id:long}")]
    [Authorize(Policy = SmsPolicies.Manage)]
    public async Task<IActionResult> DeletePurchase(long id, CancellationToken cancellationToken)
        => DeleteResult(await sms.DeletePurchaseAsync(id, cancellationToken),
            "These SMS are already allocated to organizations. Reduce the allocations first.");
}

/// <summary>SMS given to organizations from a provider's unallocated stock.</summary>
[Route("api/sms/allocations")]
[Authorize(Policy = SmsPolicies.View)]
public sealed class SmsAllocationsController(ISmsService sms) : ErpControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<SmsAllocation>>> GetAllocations([FromQuery] SmsListQueryString query, CancellationToken cancellationToken)
        => Ok(await sms.GetAllocationsAsync(query.ToQuery(), cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<SmsAllocation>> GetAllocation(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await sms.GetAllocationAsync(id, cancellationToken));

    /// <summary>Refused beyond what the provider has left to allocate.</summary>
    [HttpPost]
    [Authorize(Policy = SmsPolicies.Manage)]
    public async Task<ActionResult<SmsAllocation>> CreateAllocation([FromBody] SmsAllocationUpsertRequest request, CancellationToken cancellationToken)
    {
        var allocation = await sms.CreateAllocationAsync(request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetAllocation), new { id = allocation.Id }, allocation);
    }

    /// <summary>Also refused when the organization would be left with less than it already used.</summary>
    [HttpPut("{id:long}")]
    [Authorize(Policy = SmsPolicies.Manage)]
    public async Task<ActionResult<SmsAllocation>> UpdateAllocation(long id, [FromBody] SmsAllocationUpsertRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await sms.UpdateAllocationAsync(id, request, UserId, cancellationToken));

    [HttpDelete("{id:long}")]
    [Authorize(Policy = SmsPolicies.Manage)]
    public async Task<IActionResult> DeleteAllocation(long id, CancellationToken cancellationToken)
        => DeleteResult(await sms.DeleteAllocationAsync(id, cancellationToken),
            "The organization has already used these SMS, so the allocation can't be removed.");
}

/// <summary>Each organization's SMS: allocated, used and left. {objectId} is the organization's Object Id.</summary>
[Route("api/sms/organizations")]
[Authorize(Policy = SmsPolicies.View)]
public sealed class SmsOrganizationsController(ISmsService sms) : ErpControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<SmsOrganizationBalance>>> GetBalances(
        [FromQuery, StringLength(100)] string? search, [FromQuery] bool onlyAllocated = false,
        [FromQuery, Range(1, 500)] int limit = 50, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await sms.GetOrganizationBalancesAsync(search, onlyAllocated, limit, offset, cancellationToken));

    [HttpGet("{objectId:guid}")]
    public async Task<ActionResult<SmsOrganizationBalance>> GetBalance(Guid objectId, CancellationToken cancellationToken)
        => OkOrNotFound(await sms.GetOrganizationBalanceAsync(objectId, cancellationToken));

    /// <summary>Adds SMS the organization sent to its usage; 409 when it hasn't that many left.</summary>
    [HttpPost("{objectId:guid}/usage")]
    [Authorize(Policy = SmsPolicies.Manage)]
    public async Task<ActionResult<SmsOrganizationBalance>> AddUsage(Guid objectId, [FromBody] SmsUsageRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await sms.AddUsageAsync(objectId, request.SmsCount, cancellationToken));

    /// <summary>Corrects the organization's total usage (0 to what it was allocated).</summary>
    [HttpPut("{objectId:guid}/usage")]
    [Authorize(Policy = SmsPolicies.Manage)]
    public async Task<ActionResult<SmsOrganizationBalance>> SetUsage(Guid objectId, [FromBody] SmsUsageCorrectionRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await sms.SetUsageAsync(objectId, request.SmsUsage, cancellationToken));
}

/// <summary>
/// For integration clients with the sms.usage scope (e.g. a clinic app that sends its own SMS): the
/// balance of an organization the client created, and the SMS it sent.
/// </summary>
[Route("api/sms/integration/organizations/{objectId:guid}")]
[Authorize(Policy = IntegrationPolicies.SmsUsage)]
[EnableRateLimiting(IntegrationPolicies.RateLimit)]
public sealed class SmsIntegrationController(ISmsService sms, IntegrationClientStore clients) : ErpControllerBase
{
    [HttpGet("balance")]
    public async Task<ActionResult<SmsBalance>> GetBalance(Guid objectId, CancellationToken cancellationToken)
    {
        var client = CurrentClient();
        if (client is null)
            return Unauthorized();
        return OkOrForbidden(await sms.GetClientBalanceAsync(objectId, client.ActingUserId, cancellationToken));
    }

    /// <summary>Records SMS sent; 409 with the balance left when the organization hasn't that many.</summary>
    [HttpPost("usage")]
    public async Task<ActionResult<SmsBalance>> AddUsage(Guid objectId, [FromBody] SmsUsageRequest request, CancellationToken cancellationToken)
    {
        var client = CurrentClient();
        if (client is null)
            return Unauthorized();
        return OkOrForbidden(await sms.AddClientUsageAsync(objectId, request.SmsCount, client.ActingUserId, cancellationToken));
    }

    // Looked up again so a client removed from configuration loses its unexpired tokens too.
    private IntegrationClient? CurrentClient()
    {
        var client = clients.Find(User.FindFirst(IntegrationPolicies.ClientIdClaim)?.Value);
        return client is not null && client.Scopes.Contains(IntegrationScopes.SmsUsage) ? client : null;
    }

    private ActionResult<SmsBalance> OkOrForbidden(SmsBalance? balance)
        => balance is null
            ? StatusCode(StatusCodes.Status403Forbidden, new { error = "This organization doesn't exist or wasn't created by this client." })
            : Ok(balance);
}
