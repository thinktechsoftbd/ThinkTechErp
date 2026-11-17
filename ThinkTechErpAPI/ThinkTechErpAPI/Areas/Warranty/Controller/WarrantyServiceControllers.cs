using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Warranty;
using ThinkTechErpService.Warranty;

namespace ThinkTechErpAPI.Areas.Warranty.Controller;

/// <summary>
/// Warranty cards: one per unit sold with a warranty (issued when a sales invoice posts), plus manual cards for units
/// sold before go-live. Cancelling the invoice or taking the unit back on a sales return voids the card.
/// </summary>
[Route("api/warranty/cards")]
[Authorize(Policy = WarrantyPolicies.View)]
public sealed class WarrantyCardsController(IWarrantyCardService cards) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<WarrantyCard>>> GetCards([FromQuery] WarrantyCardListQuery query, CancellationToken cancellationToken)
        => Ok(await cards.GetCardsAsync(OrganizationId, query.ToQuery(), cancellationToken));

    /// <summary>
    /// The counter's warranty check by serial no., warranty no., invoice no. or mobile (exact): the cards with what they
    /// cover today, the units on record with their last sale, and earlier service jobs.
    /// </summary>
    [HttpGet("check")]
    public async Task<ActionResult<WarrantyCheckResult>> Check([FromQuery, Required, StringLength(100)] string q, CancellationToken cancellationToken)
        => Ok(await cards.CheckAsync(OrganizationId, q, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<WarrantyCard>> GetCard(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await cards.GetCardAsync(OrganizationId, id, cancellationToken));

    /// <summary>A card for a unit sold before the system went live (no invoice here).</summary>
    [HttpPost]
    [Authorize(Policy = WarrantyPolicies.Manage)]
    public async Task<ActionResult<WarrantyCard>> CreateCard([FromBody] WarrantyCardCreateRequest request, CancellationToken cancellationToken)
    {
        var card = await cards.CreateManualCardAsync(OrganizationId, request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetCard), new { id = card.Id }, card);
    }

    /// <summary>Voids an active card, e.g. tampered seal or physical damage.</summary>
    [HttpPost("{id:long}/void")]
    [Authorize(Policy = WarrantyPolicies.Manage)]
    public async Task<ActionResult<WarrantyCard>> VoidCard(long id, [FromBody] WarrantyCardVoidRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await cards.VoidCardAsync(OrganizationId, id, request.Reason, UserId, cancellationToken));
}

/// <summary>
/// Service jobs: a device in for repair, under warranty or paid. Received -> Diagnosing -> AwaitingApproval -> InRepair /
/// WaitingForParts / AtSupplier -> Ready -> Delivered (or Cancelled). Parts and charges are billed through a sales invoice.
/// </summary>
[Route("api/warranty/jobs")]
[Authorize(Policy = WarrantyPolicies.View)]
public sealed class ServiceJobsController(IServiceJobService jobs) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ServiceJob>>> GetJobs([FromQuery] ServiceJobListQuery query, CancellationToken cancellationToken)
        => Ok(await jobs.GetJobsAsync(OrganizationId, query.ToQuery(), cancellationToken));

    /// <summary>The job with its items, timeline and supplier claims.</summary>
    [HttpGet("{id:long}")]
    public async Task<ActionResult<ServiceJob>> GetJob(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await jobs.GetJobAsync(OrganizationId, id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = WarrantyPolicies.Manage)]
    public async Task<ActionResult<ServiceJob>> CreateJob([FromBody] ServiceJobUpsertRequest request, CancellationToken cancellationToken)
    {
        var job = await jobs.CreateJobAsync(OrganizationId, request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetJob), new { id = job.Id }, job);
    }

    /// <summary>Intake, diagnosis, technician and estimate of an open job.</summary>
    [HttpPut("{id:long}")]
    [Authorize(Policy = WarrantyPolicies.Manage)]
    public async Task<ActionResult<ServiceJob>> UpdateJob(long id, [FromBody] ServiceJobUpsertRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await jobs.UpdateJobAsync(OrganizationId, id, request, UserId, cancellationToken));

    [HttpDelete("{id:long}")]
    [Authorize(Policy = WarrantyPolicies.Manage)]
    public async Task<IActionResult> DeleteJob(long id, CancellationToken cancellationToken)
        => DeleteResult(await jobs.DeleteJobAsync(OrganizationId, id, cancellationToken),
            "Only a job still at Received, not billed and with no supplier claims, can be deleted. Cancel it instead.");

    /// <summary>Replaces the parts and service charges of an open job that isn't billed.</summary>
    [HttpPut("{id:long}/items")]
    [Authorize(Policy = WarrantyPolicies.Manage)]
    public async Task<ActionResult<ServiceJob>> SaveItems(long id, [FromBody] ServiceJobItemsRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await jobs.SaveItemsAsync(OrganizationId, id, request, UserId, cancellationToken));

    /// <summary>Moves an open job along (Ready needs a resolution); a note alone adds to the timeline.</summary>
    [HttpPost("{id:long}/status")]
    [Authorize(Policy = WarrantyPolicies.Manage)]
    public async Task<ActionResult<ServiceJob>> ChangeStatus(long id, [FromBody] ServiceJobStatusRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await jobs.ChangeStatusAsync(OrganizationId, id, request, UserId, cancellationToken));

    /// <summary>The customer's answer to the estimate; approved, an awaiting job goes to InRepair.</summary>
    [HttpPost("{id:long}/approval")]
    [Authorize(Policy = WarrantyPolicies.Manage)]
    public async Task<ActionResult<ServiceJob>> SetApproval(long id, [FromBody] ServiceJobApprovalRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await jobs.SetApprovalAsync(OrganizationId, id, request, UserId, cancellationToken));

    /// <summary>
    /// Bills the job: a sales invoice made from its items (warranty-covered lines at 0). With post=true it is completed
    /// at once: parts leave stock, and a warranty job's replacement unit gets the old card's remaining warranty.
    /// </summary>
    [HttpPost("{id:long}/invoice")]
    [Authorize(Policy = WarrantyPolicies.Manage)]
    public async Task<ActionResult<ServiceJob>> CreateInvoice(long id, [FromBody] ServiceJobInvoiceRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await jobs.CreateInvoiceAsync(OrganizationId, id, request, UserId, cancellationToken));

    /// <summary>Hands the device back. A job with parts or charges needs its invoice completed first.</summary>
    [HttpPost("{id:long}/deliver")]
    [Authorize(Policy = WarrantyPolicies.Manage)]
    public async Task<ActionResult<ServiceJob>> Deliver(long id, [FromBody] ServiceJobDeliverRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await jobs.DeliverAsync(OrganizationId, id, request, UserId, cancellationToken));

    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = WarrantyPolicies.Manage)]
    public async Task<ActionResult<ServiceJob>> Cancel(long id, [FromBody] ServiceJobCancelRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await jobs.CancelAsync(OrganizationId, id, request.Reason, UserId, cancellationToken));
}

/// <summary>Faulty units sent to the supplier, distributor or brand service center (RMA).</summary>
[Route("api/warranty/claims")]
[Authorize(Policy = WarrantyPolicies.View)]
public sealed class SupplierClaimsController(IServiceJobService jobs) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<SupplierClaim>>> GetClaims([FromQuery] SupplierClaimListQuery query, CancellationToken cancellationToken)
        => Ok(await jobs.GetClaimsAsync(OrganizationId, query.ToQuery(), cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<SupplierClaim>> GetClaim(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await jobs.GetClaimAsync(OrganizationId, id, cancellationToken));

    /// <summary>Records a unit sent to the supplier; the job it came from (still with the shop) moves to AtSupplier.</summary>
    [HttpPost]
    [Authorize(Policy = WarrantyPolicies.Manage)]
    public async Task<ActionResult<SupplierClaim>> CreateClaim([FromBody] SupplierClaimUpsertRequest request, CancellationToken cancellationToken)
    {
        var claim = await jobs.CreateClaimAsync(OrganizationId, request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetClaim), new { id = claim.Id }, claim);
    }

    /// <summary>Changes a claim still out (the job it belongs to stays).</summary>
    [HttpPut("{id:long}")]
    [Authorize(Policy = WarrantyPolicies.Manage)]
    public async Task<ActionResult<SupplierClaim>> UpdateClaim(long id, [FromBody] SupplierClaimUpsertRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await jobs.UpdateClaimAsync(OrganizationId, id, request, UserId, cancellationToken));

    /// <summary>The unit is back from the supplier; its job goes back to InRepair.</summary>
    [HttpPost("{id:long}/return")]
    [Authorize(Policy = WarrantyPolicies.Manage)]
    public async Task<ActionResult<SupplierClaim>> ReturnClaim(long id, [FromBody] SupplierClaimReturnRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await jobs.ReturnClaimAsync(OrganizationId, id, request, UserId, cancellationToken));

    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = WarrantyPolicies.Manage)]
    public async Task<ActionResult<SupplierClaim>> CancelClaim(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await jobs.CancelClaimAsync(OrganizationId, id, UserId, cancellationToken));
}

public sealed class WarrantyCardListQuery
{
    [StringLength(100)] public string? Search { get; set; }
    /// <summary>Active, Replaced or Void.</summary>
    public string? Status { get; set; }
    /// <summary>InReplacement, InWarranty, Expired, Replaced or Void (today).</summary>
    public string? Coverage { get; set; }
    /// <summary>Sale, Replacement or Manual.</summary>
    public string? Source { get; set; }
    public int? ProductId { get; set; }
    public int? CustomerId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    [Range(1, 500)] public int Limit { get; set; } = 20;
    [Range(0, int.MaxValue)] public int Offset { get; set; }

    public WarrantyCardQuery ToQuery() => new()
    {
        Search = Search, Status = Status, Coverage = Coverage, Source = Source, ProductId = ProductId,
        CustomerId = CustomerId, From = From, To = To, Limit = Limit, Offset = Offset
    };
}

public sealed class ServiceJobListQuery
{
    [StringLength(100)] public string? Search { get; set; }
    /// <summary>A status, or Open for every job still with the shop.</summary>
    public string? Status { get; set; }
    /// <summary>Warranty or Paid.</summary>
    public string? JobType { get; set; }
    public int? TechnicianUserId { get; set; }
    public int? CustomerId { get; set; }
    public long? DeviceTypeLookupDataId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    [Range(1, 500)] public int Limit { get; set; } = 20;
    [Range(0, int.MaxValue)] public int Offset { get; set; }

    public ServiceJobQuery ToQuery() => new()
    {
        Search = Search, Status = Status, JobType = JobType, TechnicianUserId = TechnicianUserId, CustomerId = CustomerId,
        DeviceTypeLookupDataId = DeviceTypeLookupDataId, From = From, To = To, Limit = Limit, Offset = Offset
    };
}

public sealed class SupplierClaimListQuery
{
    [StringLength(100)] public string? Search { get; set; }
    /// <summary>Sent, Returned or Cancelled.</summary>
    public string? Status { get; set; }
    public int? SupplierId { get; set; }
    public long? ServiceJobId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    [Range(1, 500)] public int Limit { get; set; } = 20;
    [Range(0, int.MaxValue)] public int Offset { get; set; }

    public SupplierClaimQuery ToQuery() => new()
    {
        Search = Search, Status = Status, SupplierId = SupplierId, ServiceJobId = ServiceJobId,
        From = From, To = To, Limit = Limit, Offset = Offset
    };
}
