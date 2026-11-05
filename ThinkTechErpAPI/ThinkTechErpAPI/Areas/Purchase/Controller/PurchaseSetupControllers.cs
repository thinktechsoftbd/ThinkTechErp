using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Accounting;
using ThinkTechErpDomain.Models.Purchase;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpService.Accounting;
using ThinkTechErpService.Purchase;

namespace ThinkTechErpAPI.Areas.Purchase.Controller;

[Route("api/purchase/suppliers")]
[Authorize(Policy = PurchasePolicies.View)]
public sealed class PurchaseSuppliersController(
    IPurchaseSetupService setup, IPurchaseDocumentService documents, IInvoiceVoucherService vouchers) : OrganizationControllerBase
{
    /// <summary>Sets just the supplier's ledger head (what a finalized credit purchase's payable posts to).</summary>
    [HttpPut("{supplierId:int}/ledger-head")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<IActionResult> SetLedgerHead(int supplierId, [FromBody] PartyLedgerHeadRequest request, CancellationToken cancellationToken)
        => await vouchers.SetPartyLedgerHeadAsync(OrganizationId, supplierId, request.AccSubClassId, UserId, cancellationToken)
            ? NoContent()
            : NotFound();

    [HttpGet]
    public async Task<ActionResult<PagedResult<Supplier>>> GetSuppliers(
        [FromQuery, StringLength(100)] string? search, [FromQuery] bool? isActive, [FromQuery] long? groupId,
        [FromQuery, Range(1, 500)] int limit = 50, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await setup.GetSuppliersAsync(OrganizationId, search, isActive, groupId, limit, offset, cancellationToken));

    /// <summary>Suppliers for a picker: code, name, terms and balance.</summary>
    [HttpGet("options")]
    public async Task<ActionResult<IReadOnlyList<SupplierOption>>> GetSupplierOptions(
        [FromQuery, StringLength(100)] string? search, [FromQuery, Range(1, 500)] int limit = 50,
        CancellationToken cancellationToken = default)
        => Ok(await setup.GetSupplierOptionsAsync(OrganizationId, search, limit, cancellationToken));

    [HttpGet("{supplierId:int}")]
    public async Task<ActionResult<Supplier>> GetSupplier(int supplierId, CancellationToken cancellationToken)
        => OkOrNotFound(await setup.GetSupplierAsync(OrganizationId, supplierId, cancellationToken));

    /// <summary>The supplier's posted invoices with something still to pay, oldest due first.</summary>
    [HttpGet("{supplierId:int}/open-invoices")]
    public async Task<ActionResult<IReadOnlyList<OpenPurchaseInvoice>>> GetOpenInvoices(int supplierId, CancellationToken cancellationToken)
        => Ok(await documents.GetOpenInvoicesAsync(OrganizationId, supplierId, cancellationToken));

    [HttpPost]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<Supplier>> CreateSupplier([FromBody] SupplierUpsertRequest request, CancellationToken cancellationToken)
    {
        var supplier = await setup.CreateSupplierAsync(OrganizationId, request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetSupplier), new { supplierId = supplier.Id }, supplier);
    }

    [HttpPut("{supplierId:int}")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<Supplier>> UpdateSupplier(int supplierId, [FromBody] SupplierUpsertRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await setup.UpdateSupplierAsync(OrganizationId, supplierId, request, UserId, cancellationToken));

    [HttpDelete("{supplierId:int}")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<IActionResult> DeleteSupplier(int supplierId, CancellationToken cancellationToken)
        => DeleteResult(await setup.DeleteSupplierAsync(OrganizationId, supplierId, cancellationToken),
            "The supplier has purchase documents. Deactivate it instead.");
}

[Route("api/purchase/tax-rates")]
[Authorize(Policy = PurchasePolicies.View)]
public sealed class PurchaseTaxRatesController(IPurchaseSetupService setup) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<TaxRate>>> GetTaxRates(
        [FromQuery, StringLength(100)] string? search, [FromQuery] bool? isActive,
        [FromQuery, Range(1, 500)] int limit = 50, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await setup.GetTaxRatesAsync(OrganizationId, search, isActive, limit, offset, cancellationToken));

    [HttpGet("{taxRateId:int}")]
    public async Task<ActionResult<TaxRate>> GetTaxRate(int taxRateId, CancellationToken cancellationToken)
        => OkOrNotFound(await setup.GetTaxRateAsync(OrganizationId, taxRateId, cancellationToken));

    [HttpPost]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<TaxRate>> CreateTaxRate([FromBody] TaxRateUpsertRequest request, CancellationToken cancellationToken)
    {
        var rate = await setup.CreateTaxRateAsync(OrganizationId, request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetTaxRate), new { taxRateId = rate.Id }, rate);
    }

    [HttpPut("{taxRateId:int}")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<TaxRate>> UpdateTaxRate(int taxRateId, [FromBody] TaxRateUpsertRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await setup.UpdateTaxRateAsync(OrganizationId, taxRateId, request, UserId, cancellationToken));

    [HttpDelete("{taxRateId:int}")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<IActionResult> DeleteTaxRate(int taxRateId, CancellationToken cancellationToken)
        => DeleteResult(await setup.DeleteTaxRateAsync(OrganizationId, taxRateId, cancellationToken),
            "Tax groups or invoices use this rate. Deactivate it instead.");
}

[Route("api/purchase/tax-groups")]
[Authorize(Policy = PurchasePolicies.View)]
public sealed class PurchaseTaxGroupsController(IPurchaseSetupService setup) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<TaxGroup>>> GetTaxGroups(
        [FromQuery, StringLength(100)] string? search, [FromQuery] bool? isActive,
        [FromQuery, Range(1, 500)] int limit = 50, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await setup.GetTaxGroupsAsync(OrganizationId, search, isActive, limit, offset, cancellationToken));

    [HttpGet("{taxGroupId:int}")]
    public async Task<ActionResult<TaxGroup>> GetTaxGroup(int taxGroupId, CancellationToken cancellationToken)
        => OkOrNotFound(await setup.GetTaxGroupAsync(OrganizationId, taxGroupId, cancellationToken));

    [HttpPost]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<TaxGroup>> CreateTaxGroup([FromBody] TaxGroupUpsertRequest request, CancellationToken cancellationToken)
    {
        var group = await setup.CreateTaxGroupAsync(OrganizationId, request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetTaxGroup), new { taxGroupId = group.Id }, group);
    }

    [HttpPut("{taxGroupId:int}")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<TaxGroup>> UpdateTaxGroup(int taxGroupId, [FromBody] TaxGroupUpsertRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await setup.UpdateTaxGroupAsync(OrganizationId, taxGroupId, request, UserId, cancellationToken));

    [HttpDelete("{taxGroupId:int}")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<IActionResult> DeleteTaxGroup(int taxGroupId, CancellationToken cancellationToken)
        => DeleteResult(await setup.DeleteTaxGroupAsync(OrganizationId, taxGroupId, cancellationToken),
            "Products or documents use this tax group. Deactivate it instead.");
}
