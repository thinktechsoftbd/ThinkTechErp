using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Purchase;
using ThinkTechErpDomain.Models.Sales;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Warranty;
using ThinkTechErpService.Warranty;

namespace ThinkTechErpAPI.Areas.Warranty.Controller;

/// <summary>Warranty policies: the terms a product is sold with, e.g. "3 Years (1 Year Replacement)".</summary>
[Route("api/warranty/policies")]
[Authorize(Policy = WarrantyPolicies.View)]
public sealed class WarrantyPoliciesController(IWarrantySetupService setup) : OrganizationControllerBase
{
    /// <summary>
    /// A page of policies; <c>sort</c> is name, period, products, status or updated (default: period, then name),
    /// <c>direction</c> asc or desc.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<WarrantyPolicy>>> GetPolicies(
        [FromQuery, StringLength(100)] string? search, [FromQuery] bool? isActive,
        [FromQuery, RegularExpression("^(name|period|products|status|updated)$")] string? sort = null,
        [FromQuery, RegularExpression("^(asc|desc)$")] string direction = "asc",
        [FromQuery, Range(1, 500)] int limit = 50, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await setup.GetPoliciesAsync(OrganizationId, search, isActive, sort, direction == "desc", limit, offset, cancellationToken));

    /// <summary>Policies for a picker (active ones unless includeInactive).</summary>
    [HttpGet("options")]
    public async Task<ActionResult<IReadOnlyList<WarrantyPolicyOption>>> GetPolicyOptions(
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default)
        => Ok(await setup.GetPolicyOptionsAsync(OrganizationId, includeInactive, cancellationToken));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<WarrantyPolicy>> GetPolicy(int id, CancellationToken cancellationToken)
        => OkOrNotFound(await setup.GetPolicyAsync(OrganizationId, id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = WarrantyPolicies.Manage)]
    public async Task<ActionResult<WarrantyPolicy>> CreatePolicy([FromBody] WarrantyPolicyUpsertRequest request, CancellationToken cancellationToken)
    {
        var policy = await setup.CreatePolicyAsync(OrganizationId, request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetPolicy), new { id = policy.Id }, policy);
    }

    /// <summary>Cards already issued keep the terms they were sold with; the change applies to later sales.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = WarrantyPolicies.Manage)]
    public async Task<ActionResult<WarrantyPolicy>> UpdatePolicy(int id, [FromBody] WarrantyPolicyUpsertRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await setup.UpdatePolicyAsync(OrganizationId, id, request, UserId, cancellationToken));

    [HttpDelete("{id:int}")]
    [Authorize(Policy = WarrantyPolicies.Manage)]
    public async Task<IActionResult> DeletePolicy(int id, CancellationToken cancellationToken)
        => DeleteResult(await setup.DeletePolicyAsync(OrganizationId, id, cancellationToken),
            "Products or warranty cards use this policy. Deactivate it instead.");
}

/// <summary>Which products are sold with which warranty.</summary>
[Route("api/warranty/products")]
[Authorize(Policy = WarrantyPolicies.View)]
public sealed class WarrantyProductsController(IWarrantySetupService setup) : OrganizationControllerBase
{
    /// <summary>Products with their warranty policy; hasPolicy=false lists those sold without one.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductWarrantyItem>>> GetProducts(
        [FromQuery, StringLength(100)] string? search, [FromQuery] int? categoryId, [FromQuery] int? brandId,
        [FromQuery] int? warrantyPolicyId, [FromQuery] bool? hasPolicy,
        [FromQuery, Range(1, 500)] int limit = 50, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await setup.GetProductsAsync(OrganizationId, new ProductWarrantyQuery
        {
            Search = search,
            CategoryId = categoryId,
            BrandId = brandId,
            WarrantyPolicyId = warrantyPolicyId,
            HasPolicy = hasPolicy,
            Limit = limit,
            Offset = offset
        }, cancellationToken));

    /// <summary>Sets the policy of these products (warrantyPolicyId null takes it off). Returns how many changed.</summary>
    [HttpPut("policy")]
    [Authorize(Policy = WarrantyPolicies.Manage)]
    public async Task<ActionResult<object>> AssignPolicy([FromBody] ProductWarrantyAssignRequest request, CancellationToken cancellationToken)
        => Ok(new { changed = await setup.AssignPolicyAsync(OrganizationId, request, UserId, cancellationToken) });
}

/// <summary>Pickers the warranty screens need, open to warranty users without Sales or Purchase access.</summary>
[Route("api/warranty/lookups")]
[Authorize(Policy = WarrantyPolicies.View)]
public sealed class WarrantyLookupsController(IWarrantySetupService setup) : OrganizationControllerBase
{
    /// <summary>The Service Device Type lookup: Laptop, Desktop PC, CC Camera, Printer, Mobile, Other.</summary>
    [HttpGet("device-types")]
    public async Task<ActionResult<IReadOnlyList<OptionItem>>> GetDeviceTypes(CancellationToken cancellationToken)
        => Ok(await setup.GetDeviceTypesAsync(cancellationToken));

    /// <summary>Active users who work in the organization (Code is the e-mail).</summary>
    [HttpGet("technicians")]
    public async Task<ActionResult<IReadOnlyList<OptionItem>>> GetTechnicians(CancellationToken cancellationToken)
        => Ok(await setup.GetTechniciansAsync(OrganizationId, cancellationToken));

    /// <summary>Customers (and suppliers, who can buy too) by code, name or mobile; or exactly these ids.</summary>
    [HttpGet("customers")]
    public async Task<ActionResult<IReadOnlyList<CustomerOption>>> GetCustomers(
        [FromQuery, StringLength(100)] string? search, [FromQuery, StringLength(500)] string? ids,
        [FromQuery, Range(1, 500)] int limit = 20, CancellationToken cancellationToken = default)
    {
        var customerIds = ids?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => int.TryParse(id, out var value) ? value : 0).Where(id => id > 0).Distinct().Take(100).ToList();
        return Ok(await setup.GetCustomerOptionsAsync(OrganizationId, search, customerIds, limit, cancellationToken));
    }

    /// <summary>Suppliers, for claims.</summary>
    [HttpGet("suppliers")]
    public async Task<ActionResult<IReadOnlyList<SupplierOption>>> GetSuppliers(
        [FromQuery, StringLength(100)] string? search, [FromQuery, Range(1, 500)] int limit = 20,
        CancellationToken cancellationToken = default)
        => Ok(await setup.GetSupplierOptionsAsync(OrganizationId, search, limit, cancellationToken));
}
