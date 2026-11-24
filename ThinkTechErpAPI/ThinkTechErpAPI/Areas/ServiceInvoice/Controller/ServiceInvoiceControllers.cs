using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Sales;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.ServiceInvoices;
using ThinkTechErpService.ServiceInvoices;
// The area's namespace (ThinkTechErpAPI.Areas.ServiceInvoice) would hide the model type of the same name.
using ServiceInvoiceModel = ThinkTechErpDomain.Models.ServiceInvoices.ServiceInvoice;

namespace ThinkTechErpAPI.Areas.ServiceInvoice.Controller;

/// <summary>Query string of the service invoice list.</summary>
public sealed class ServiceInvoiceListQuery
{
    [StringLength(100)] public string? Search { get; set; }
    public string? Status { get; set; }
    public int? CustomerId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? SaleType { get; set; }
    public string? PaymentStatus { get; set; }
    /// <summary>number, date, customer, type, service, total, due, payment or status. Others are ignored (newest first).</summary>
    [RegularExpression("^[a-z]{1,20}$")] public string? Sort { get; set; }
    [RegularExpression("^(asc|desc)$")] public string? Direction { get; set; }
    [Range(1, 500)] public int Limit { get; set; } = 20;
    [Range(0, int.MaxValue)] public int Offset { get; set; }

    public ServiceInvoiceQuery ToQuery() => new()
    {
        Search = Search,
        Status = Status,
        CustomerId = CustomerId,
        From = From,
        To = To,
        SaleType = SaleType,
        PaymentStatus = PaymentStatus,
        Sort = Sort,
        Descending = Direction == "desc",
        Limit = Limit,
        Offset = Offset
    };
}

/// <summary>
/// Service invoices (Warranty -> Transactions): the parts used on a job, the service charge and a note of the work,
/// cash or credit. Saved as drafts: no stock moves and no due goes to the customer's ledger.
/// </summary>
[Route("api/service-invoices")]
[Authorize(Policy = ServiceInvoicePolicies.View)]
public sealed class ServiceInvoicesController(IServiceInvoiceService invoices) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ServiceInvoiceModel>>> GetInvoices([FromQuery] ServiceInvoiceListQuery query, CancellationToken cancellationToken)
        => Ok(await invoices.GetInvoicesAsync(OrganizationId, query.ToQuery(), cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ServiceInvoiceModel>> GetInvoice(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await invoices.GetInvoiceAsync(OrganizationId, id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = ServiceInvoicePolicies.Manage)]
    public async Task<ActionResult<ServiceInvoiceModel>> CreateInvoice([FromBody] ServiceInvoiceUpsertRequest request, CancellationToken cancellationToken)
    {
        var invoice = await invoices.CreateInvoiceAsync(OrganizationId, request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetInvoice), new { id = invoice.Id }, invoice);
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = ServiceInvoicePolicies.Manage)]
    public async Task<ActionResult<ServiceInvoiceModel>> UpdateInvoice(long id, [FromBody] ServiceInvoiceUpsertRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await invoices.UpdateInvoiceAsync(OrganizationId, id, request, UserId, cancellationToken));

    [HttpDelete("{id:long}")]
    [Authorize(Policy = ServiceInvoicePolicies.Manage)]
    public async Task<IActionResult> DeleteInvoice(long id, CancellationToken cancellationToken)
        => DeleteResult(await invoices.DeleteInvoiceAsync(OrganizationId, id, cancellationToken),
            "Only a draft service invoice can be deleted.");
}

/// <summary>
/// Pickers of the service invoice screen that need no Sales access. Products, warehouses, tax groups and settings come
/// from the shared inventory lookups (api/inventory, InventoryPolicies.Lookup), which SERVICE_INVOICE.V/M also open.
/// </summary>
[Route("api/service-invoices/lookups")]
[Authorize(Policy = ServiceInvoicePolicies.View)]
public sealed class ServiceInvoiceLookupsController(IServiceInvoiceService invoices) : OrganizationControllerBase
{
    /// <summary>Customers (and suppliers, who can be billed too) by code, name or mobile; or exactly these ids.</summary>
    [HttpGet("customers")]
    public async Task<ActionResult<IReadOnlyList<CustomerOption>>> GetCustomers(
        [FromQuery, StringLength(100)] string? search, [FromQuery, StringLength(500)] string? ids,
        [FromQuery, Range(1, 500)] int limit = 20, CancellationToken cancellationToken = default)
    {
        var customerIds = ids?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => int.TryParse(id, out var value) ? value : 0).Where(id => id > 0).Distinct().Take(100).ToList();
        return Ok(await invoices.GetCustomerOptionsAsync(OrganizationId, search, customerIds, limit, cancellationToken));
    }
}
