using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Sales;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpService.Sales;

namespace ThinkTechErpAPI.Areas.Sales.Controller;

[Route("api/sales/customers")]
[Authorize(Policy = SalesPolicies.View)]
public sealed class SalesCustomersController(ISalesSetupService setup, ISalesDocumentService documents) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<Customer>>> GetCustomers(
        [FromQuery, StringLength(100)] string? search, [FromQuery] bool? isActive, [FromQuery] long? groupId,
        [FromQuery, Range(1, 500)] int limit = 50, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await setup.GetCustomersAsync(OrganizationId, search, isActive, groupId, limit, offset, cancellationToken));

    /// <summary>
    /// Customers for the sales screen's picker (by code, name or mobile), with what they owe and the credit
    /// left. With ids (comma separated) it returns exactly those customers.
    /// </summary>
    [HttpGet("options")]
    public async Task<ActionResult<IReadOnlyList<CustomerOption>>> GetCustomerOptions(
        [FromQuery, StringLength(100)] string? search, [FromQuery, StringLength(500)] string? ids,
        [FromQuery, Range(1, 500)] int limit = 20, CancellationToken cancellationToken = default)
    {
        var customerIds = ids?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => int.TryParse(id, out var value) ? value : 0).Where(id => id > 0).Distinct().Take(100).ToList();
        return Ok(await setup.GetCustomerOptionsAsync(OrganizationId, search, customerIds, limit, cancellationToken));
    }

    [HttpGet("{customerId:int}")]
    public async Task<ActionResult<Customer>> GetCustomer(int customerId, CancellationToken cancellationToken)
        => OkOrNotFound(await setup.GetCustomerAsync(OrganizationId, customerId, cancellationToken));

    /// <summary>The customer's posted invoices with something still to collect, oldest due first.</summary>
    [HttpGet("{customerId:int}/open-invoices")]
    public async Task<ActionResult<IReadOnlyList<OpenSalesInvoice>>> GetOpenInvoices(int customerId, CancellationToken cancellationToken)
        => Ok(await documents.GetOpenInvoicesAsync(OrganizationId, customerId, cancellationToken));

    /// <summary>Adds a customer; an empty code takes the next one (C-00001, ...), so the counter can add one quickly.</summary>
    [HttpPost]
    [Authorize(Policy = SalesPolicies.Manage)]
    public async Task<ActionResult<Customer>> CreateCustomer([FromBody] CustomerUpsertRequest request, CancellationToken cancellationToken)
    {
        var customer = await setup.CreateCustomerAsync(OrganizationId, request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetCustomer), new { customerId = customer.Id }, customer);
    }

    [HttpPut("{customerId:int}")]
    [Authorize(Policy = SalesPolicies.Manage)]
    public async Task<ActionResult<Customer>> UpdateCustomer(int customerId, [FromBody] CustomerUpsertRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await setup.UpdateCustomerAsync(OrganizationId, customerId, request, UserId, cancellationToken));

    [HttpDelete("{customerId:int}")]
    [Authorize(Policy = SalesPolicies.Manage)]
    public async Task<IActionResult> DeleteCustomer(int customerId, CancellationToken cancellationToken)
        => DeleteResult(await setup.DeleteCustomerAsync(OrganizationId, customerId, cancellationToken),
            "The customer has sales documents. Deactivate it instead.");
}
