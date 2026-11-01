using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Inventory;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Inventory;
using ThinkTechErpService.Inventory;

namespace ThinkTechErpAPI.Areas.Inventory.Controller;

/// <summary>Stock adjustments, including opening stock (reason Opening). Drafts are posted to move stock.</summary>
[Route("api/inventory/adjustments")]
[Authorize(Policy = InventoryPolicies.View)]
public sealed class InventoryAdjustmentsController(IStockDocumentService documents) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<StockAdjustment>>> GetAdjustments(
        [FromQuery, StringLength(100)] string? search, [FromQuery] string? status, [FromQuery] int? warehouseId,
        [FromQuery] string? reason, [FromQuery] bool excludeOpening = false, [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null, [FromQuery, Range(1, 500)] int limit = 20, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await documents.GetAdjustmentsAsync(OrganizationId, new StockDocumentQuery
        {
            Search = search,
            Status = status,
            WarehouseId = warehouseId,
            Reason = reason,
            ExcludeOpening = excludeOpening,
            From = from,
            To = to,
            Limit = limit,
            Offset = offset
        }, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<StockAdjustment>> GetAdjustment(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.GetAdjustmentAsync(OrganizationId, id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<StockAdjustment>> CreateAdjustment([FromBody] StockAdjustmentUpsertRequest request, CancellationToken cancellationToken)
    {
        var adjustment = await documents.CreateAdjustmentAsync(OrganizationId, request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetAdjustment), new { id = adjustment.Id }, adjustment);
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<StockAdjustment>> UpdateAdjustment(long id, [FromBody] StockAdjustmentUpsertRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.UpdateAdjustmentAsync(OrganizationId, id, request, UserId, cancellationToken));

    [HttpDelete("{id:long}")]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<IActionResult> DeleteAdjustment(long id, CancellationToken cancellationToken)
        => DeleteResult(await documents.DeleteAdjustmentAsync(OrganizationId, id, cancellationToken),
            "Only a draft adjustment can be deleted; cancel a posted one instead.");

    [HttpPost("{id:long}/post")]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<StockAdjustment>> PostAdjustment(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.PostAdjustmentAsync(OrganizationId, id, UserId, cancellationToken));

    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<StockAdjustment>> CancelAdjustment(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.CancelAdjustmentAsync(OrganizationId, id, UserId, cancellationToken));
}

/// <summary>Stock moved between warehouses: draft, then dispatched (in transit), then received.</summary>
[Route("api/inventory/transfers")]
[Authorize(Policy = InventoryPolicies.View)]
public sealed class InventoryTransfersController(IStockDocumentService documents) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<StockTransfer>>> GetTransfers(
        [FromQuery, StringLength(100)] string? search, [FromQuery] string? status, [FromQuery] int? warehouseId,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery, Range(1, 500)] int limit = 20, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await documents.GetTransfersAsync(OrganizationId, new StockDocumentQuery
        {
            Search = search,
            Status = status,
            WarehouseId = warehouseId,
            From = from,
            To = to,
            Limit = limit,
            Offset = offset
        }, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<StockTransfer>> GetTransfer(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.GetTransferAsync(OrganizationId, id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<StockTransfer>> CreateTransfer([FromBody] StockTransferUpsertRequest request, CancellationToken cancellationToken)
    {
        var transfer = await documents.CreateTransferAsync(OrganizationId, request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetTransfer), new { id = transfer.Id }, transfer);
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<StockTransfer>> UpdateTransfer(long id, [FromBody] StockTransferUpsertRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.UpdateTransferAsync(OrganizationId, id, request, UserId, cancellationToken));

    [HttpDelete("{id:long}")]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<IActionResult> DeleteTransfer(long id, CancellationToken cancellationToken)
        => DeleteResult(await documents.DeleteTransferAsync(OrganizationId, id, cancellationToken),
            "Only a draft transfer can be deleted; cancel a dispatched one instead.");

    [HttpPost("{id:long}/dispatch")]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<StockTransfer>> DispatchTransfer(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.DispatchTransferAsync(OrganizationId, id, UserId, cancellationToken));

    [HttpPost("{id:long}/receive")]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<StockTransfer>> ReceiveTransfer(long id, [FromBody] StockTransferReceiveRequest? request, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.ReceiveTransferAsync(OrganizationId, id, request ?? new StockTransferReceiveRequest(), UserId, cancellationToken));

    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<StockTransfer>> CancelTransfer(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.CancelTransferAsync(OrganizationId, id, UserId, cancellationToken));
}

/// <summary>Physical stock counts: started (expected quantities frozen), counted, then posted as an adjustment.</summary>
[Route("api/inventory/counts")]
[Authorize(Policy = InventoryPolicies.View)]
public sealed class InventoryCountsController(IStockDocumentService documents) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<StockCount>>> GetCounts(
        [FromQuery, StringLength(100)] string? search, [FromQuery] string? status, [FromQuery] int? warehouseId,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery, Range(1, 500)] int limit = 20, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await documents.GetCountsAsync(OrganizationId, new StockDocumentQuery
        {
            Search = search,
            Status = status,
            WarehouseId = warehouseId,
            From = from,
            To = to,
            Limit = limit,
            Offset = offset
        }, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<StockCount>> GetCount(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.GetCountAsync(OrganizationId, id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<StockCount>> CreateCount([FromBody] StockCountCreateRequest request, CancellationToken cancellationToken)
    {
        var count = await documents.CreateCountAsync(OrganizationId, request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetCount), new { id = count.Id }, count);
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<StockCount>> UpdateCount(long id, [FromBody] StockCountUpdateRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.UpdateCountAsync(OrganizationId, id, request, UserId, cancellationToken));

    [HttpPost("{id:long}/post")]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<StockCount>> PostCount(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.PostCountAsync(OrganizationId, id, UserId, cancellationToken));

    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = InventoryPolicies.Manage)]
    public async Task<ActionResult<StockCount>> CancelCount(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.CancelCountAsync(OrganizationId, id, UserId, cancellationToken));
}

[Route("api/inventory/reports")]
[Authorize(Policy = InventoryPolicies.View)]
public sealed class InventoryReportsController(IInventoryReportService reports) : OrganizationControllerBase
{
    [HttpGet("stock-on-hand")]
    public async Task<ActionResult<ReportPage<StockOnHandRow>>> GetStockOnHand(
        [FromQuery, StringLength(100)] string? search, [FromQuery] int? warehouseId, [FromQuery] int? categoryId,
        [FromQuery] bool lowStockOnly = false, [FromQuery] bool includeZero = false, [FromQuery] bool byWarehouse = false,
        [FromQuery, Range(1, 1000)] int limit = 50, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await reports.GetStockOnHandAsync(OrganizationId, new StockReportQuery
        {
            Search = search,
            WarehouseId = warehouseId,
            LowStockOnly = lowStockOnly,
            IncludeZero = includeZero,
            ByWarehouse = byWarehouse,
            Limit = limit,
            Offset = offset
        }, categoryId, cancellationToken));

    [HttpGet("stock-card")]
    public async Task<ActionResult<StockCard>> GetStockCard(
        [FromQuery, Range(1, int.MaxValue)] int productId, [FromQuery] int? warehouseId, [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to, CancellationToken cancellationToken)
        => OkOrNotFound(await reports.GetStockCardAsync(OrganizationId, productId, warehouseId, from, to, cancellationToken));

    /// <summary>Stock value grouped by category (default), warehouse or brand.</summary>
    [HttpGet("valuation")]
    public async Task<ActionResult<IReadOnlyList<StockValuationRow>>> GetValuation(
        [FromQuery] string? groupBy, [FromQuery] int? warehouseId, CancellationToken cancellationToken)
        => Ok(await reports.GetValuationAsync(OrganizationId, groupBy, warehouseId, cancellationToken));

    [HttpGet("movements")]
    public async Task<ActionResult<ReportPage<StockMovementRow>>> GetMovements(
        [FromQuery, StringLength(100)] string? search, [FromQuery] int? warehouseId, [FromQuery] int? productId,
        [FromQuery] string? movementType, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery, Range(1, 1000)] int limit = 50, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var (start, end) = InventoryReportService.Period(from, to);
        return Ok(await reports.GetMovementsAsync(OrganizationId, new MovementReportQuery
        {
            Search = search,
            WarehouseId = warehouseId,
            ProductId = productId,
            MovementType = movementType,
            From = start,
            To = end,
            Limit = limit,
            Offset = offset
        }, cancellationToken));
    }

    [HttpGet("reorder")]
    public async Task<ActionResult<ReportPage<ReorderRow>>> GetReorder(
        [FromQuery, StringLength(100)] string? search, [FromQuery] int? warehouseId, [FromQuery] int? categoryId,
        [FromQuery, Range(1, 1000)] int limit = 50, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await reports.GetReorderAsync(OrganizationId, new StockReportQuery
        {
            Search = search,
            WarehouseId = warehouseId,
            Limit = limit,
            Offset = offset
        }, categoryId, cancellationToken));

    /// <summary>Batches with stock that expire within withinDays (default: the inventory setting), expired ones included.</summary>
    [HttpGet("expiry")]
    public async Task<ActionResult<ReportPage<ExpiryRow>>> GetExpiry(
        [FromQuery, Range(0, 3650)] int? withinDays, [FromQuery] int? warehouseId, [FromQuery, StringLength(100)] string? search,
        [FromQuery, Range(1, 1000)] int limit = 50, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await reports.GetExpiryAsync(OrganizationId, withinDays, warehouseId, search, limit, offset, cancellationToken));
}
