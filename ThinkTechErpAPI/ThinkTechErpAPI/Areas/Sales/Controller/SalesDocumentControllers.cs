using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Inventory;
using ThinkTechErpDomain.Models.Purchase;
using ThinkTechErpDomain.Models.Sales;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Sales;
using ThinkTechErpService.Accounting;
using ThinkTechErpService.Sales;

namespace ThinkTechErpAPI.Areas.Sales.Controller;

/// <summary>Query string shared by the sales document lists.</summary>
public sealed class SalesListQuery
{
    [StringLength(100)] public string? Search { get; set; }
    public string? Status { get; set; }
    public int? CustomerId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? SaleType { get; set; }
    public string? PaymentStatus { get; set; }
    [Range(1, 500)] public int Limit { get; set; } = 20;
    [Range(0, int.MaxValue)] public int Offset { get; set; }

    public SalesDocumentQuery ToQuery() => new()
    {
        Search = Search,
        Status = Status,
        CustomerId = CustomerId,
        From = From,
        To = To,
        SaleType = SaleType,
        PaymentStatus = PaymentStatus,
        Limit = Limit,
        Offset = Offset
    };
}

/// <summary>
/// Sales invoices (cash or credit). Posting takes the stock out at the average cost; a credit sale adds
/// the unpaid part to the customer's dues, within their credit limit.
/// </summary>
[Route("api/sales/invoices")]
[Authorize(Policy = SalesPolicies.View)]
public sealed class SalesInvoicesController(ISalesDocumentService documents, IInvoiceVoucherService finalizer) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<SalesInvoice>>> GetInvoices([FromQuery] SalesListQuery query, CancellationToken cancellationToken)
        => Ok(await documents.GetInvoicesAsync(OrganizationId, query.ToQuery(), cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<SalesInvoice>> GetInvoice(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.GetInvoiceAsync(OrganizationId, id, cancellationToken));

    /// <summary>Saves a draft; with post=true it is posted in the same call (the usual counter sale).</summary>
    [HttpPost]
    [Authorize(Policy = SalesPolicies.Manage)]
    public async Task<ActionResult<SalesInvoice>> CreateInvoice(
        [FromBody] SalesInvoiceUpsertRequest request, [FromQuery] bool post = false, CancellationToken cancellationToken = default)
    {
        var invoice = await documents.CreateInvoiceAsync(OrganizationId, request, UserId, cancellationToken);
        if (post)
            invoice = (await documents.PostInvoiceAsync(OrganizationId, invoice.Id, UserId, cancellationToken))!;
        return CreatedAtAction(nameof(GetInvoice), new { id = invoice.Id }, invoice);
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = SalesPolicies.Manage)]
    public async Task<ActionResult<SalesInvoice>> UpdateInvoice(
        long id, [FromBody] SalesInvoiceUpsertRequest request, [FromQuery] bool post = false, CancellationToken cancellationToken = default)
    {
        var invoice = await documents.UpdateInvoiceAsync(OrganizationId, id, request, UserId, cancellationToken);
        if (invoice is not null && post)
            invoice = await documents.PostInvoiceAsync(OrganizationId, id, UserId, cancellationToken);
        return OkOrNotFound(invoice);
    }

    [HttpDelete("{id:long}")]
    [Authorize(Policy = SalesPolicies.Manage)]
    public async Task<IActionResult> DeleteInvoice(long id, CancellationToken cancellationToken)
        => DeleteResult(await documents.DeleteInvoiceAsync(OrganizationId, id, cancellationToken),
            "Only a draft invoice can be deleted; cancel a posted one instead.");

    [HttpPost("{id:long}/post")]
    [Authorize(Policy = SalesPolicies.Manage)]
    public async Task<ActionResult<SalesInvoice>> PostInvoice(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.PostInvoiceAsync(OrganizationId, id, UserId, cancellationToken));

    /// <summary>
    /// Posts the invoice if it is a draft, then posts its Bill Voucher from the ledger mappings (customer or the
    /// cash sales head, item categories). Refused, with what is missing, unless every mapping is there. The invoice
    /// can't be cancelled while the voucher is there.
    /// </summary>
    [HttpPost("{id:long}/finalize")]
    [Authorize(Policy = SalesPolicies.Manage)]
    public async Task<ActionResult<SalesInvoice>> FinalizeInvoice(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await finalizer.FinalizeSalesInvoiceAsync(OrganizationId, id, UserId, cancellationToken));

    /// <summary>Puts the stock back at the cost it left at; refused while receipts settle the invoice or it is finalized.</summary>
    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = SalesPolicies.Manage)]
    public async Task<ActionResult<SalesInvoice>> CancelInvoice(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.CancelInvoiceAsync(OrganizationId, id, UserId, cancellationToken));
}

/// <summary>
/// Goods a customer brings back, against the sale's memo. Posting puts resellable goods back into stock at
/// the cost they left at, and either refunds the money or deducts it from the customer's dues.
/// </summary>
[Route("api/sales/returns")]
[Authorize(Policy = SalesPolicies.View)]
public sealed class SalesReturnsController(ISalesDocumentService documents) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<SalesReturn>>> GetReturns([FromQuery] SalesListQuery query, CancellationToken cancellationToken)
        => Ok(await documents.GetReturnsAsync(OrganizationId, query.ToQuery(), cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<SalesReturn>> GetReturn(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.GetReturnAsync(OrganizationId, id, cancellationToken));

    /// <summary>Saves a draft; with post=true it is posted in the same call.</summary>
    [HttpPost]
    [Authorize(Policy = SalesPolicies.Manage)]
    public async Task<ActionResult<SalesReturn>> CreateReturn(
        [FromBody] SalesReturnUpsertRequest request, [FromQuery] bool post = false, CancellationToken cancellationToken = default)
    {
        var salesReturn = await documents.CreateReturnAsync(OrganizationId, request, UserId, cancellationToken);
        if (post)
            salesReturn = (await documents.PostReturnAsync(OrganizationId, salesReturn.Id, UserId, cancellationToken))!;
        return CreatedAtAction(nameof(GetReturn), new { id = salesReturn.Id }, salesReturn);
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = SalesPolicies.Manage)]
    public async Task<ActionResult<SalesReturn>> UpdateReturn(
        long id, [FromBody] SalesReturnUpsertRequest request, [FromQuery] bool post = false, CancellationToken cancellationToken = default)
    {
        var salesReturn = await documents.UpdateReturnAsync(OrganizationId, id, request, UserId, cancellationToken);
        if (salesReturn is not null && post)
            salesReturn = await documents.PostReturnAsync(OrganizationId, id, UserId, cancellationToken);
        return OkOrNotFound(salesReturn);
    }

    [HttpDelete("{id:long}")]
    [Authorize(Policy = SalesPolicies.Manage)]
    public async Task<IActionResult> DeleteReturn(long id, CancellationToken cancellationToken)
        => DeleteResult(await documents.DeleteReturnAsync(OrganizationId, id, cancellationToken),
            "Only a draft return can be deleted; cancel a posted one instead.");

    [HttpPost("{id:long}/post")]
    [Authorize(Policy = SalesPolicies.Manage)]
    public async Task<ActionResult<SalesReturn>> PostReturn(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.PostReturnAsync(OrganizationId, id, UserId, cancellationToken));

    /// <summary>Takes the goods back out of stock and undoes the refund or deduction.</summary>
    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = SalesPolicies.Manage)]
    public async Task<ActionResult<SalesReturn>> CancelReturn(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.CancelReturnAsync(OrganizationId, id, UserId, cancellationToken));
}

/// <summary>Money received from customers after the sale (dues collection), recorded and posted in one step.</summary>
[Route("api/sales/receipts")]
[Authorize(Policy = SalesPolicies.View)]
public sealed class SalesReceiptsController(ISalesDocumentService documents) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<CustomerReceipt>>> GetReceipts([FromQuery] SalesListQuery query, CancellationToken cancellationToken)
        => Ok(await documents.GetReceiptsAsync(OrganizationId, query.ToQuery(), cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<CustomerReceipt>> GetReceipt(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.GetReceiptAsync(OrganizationId, id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = SalesPolicies.Manage)]
    public async Task<ActionResult<CustomerReceipt>> CreateReceipt([FromBody] CustomerReceiptUpsertRequest request, CancellationToken cancellationToken)
    {
        var receipt = await documents.CreateReceiptAsync(OrganizationId, request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetReceipt), new { id = receipt.Id }, receipt);
    }

    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = SalesPolicies.Manage)]
    public async Task<ActionResult<CustomerReceipt>> CancelReceipt(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.CancelReceiptAsync(OrganizationId, id, UserId, cancellationToken));

    /// <summary>Pending, Cleared or Bounced. Bounced takes the receipt back off the invoices.</summary>
    [HttpPut("{id:long}/cheque-status")]
    [Authorize(Policy = SalesPolicies.Manage)]
    public async Task<ActionResult<CustomerReceipt>> SetChequeStatus(long id, [FromBody] ChequeStatusRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.SetChequeStatusAsync(OrganizationId, id, request.ChequeStatus, UserId, cancellationToken));
}

[Route("api/sales/reports")]
[Authorize(Policy = SalesPolicies.View)]
public sealed class SalesReportsController(ISalesReportService reports) : OrganizationControllerBase
{
    [HttpGet("customer-ledger")]
    public async Task<ActionResult<CustomerLedger>> GetCustomerLedger(
        [FromQuery, Range(1, int.MaxValue)] int customerId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
        => OkOrNotFound(await reports.GetCustomerLedgerAsync(OrganizationId, customerId, from, to, cancellationToken));

    /// <summary>What each customer owes, with unpaid invoices aged by days past due.</summary>
    [HttpGet("customer-dues")]
    public async Task<ActionResult<ReportPage<CustomerDueRow>>> GetCustomerDues(
        [FromQuery, StringLength(100)] string? search, [FromQuery] DateOnly? asOf, [FromQuery] bool onlyWithBalance = true,
        [FromQuery, Range(1, 1000)] int limit = 50, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await reports.GetCustomerDuesAsync(OrganizationId, new SalesReportQuery
        {
            Search = search,
            OnlyWithBalance = onlyWithBalance,
            Limit = limit,
            Offset = offset
        }, asOf, cancellationToken));

    /// <summary>Posted sales invoices in a period, with cost of goods sold and profit.</summary>
    [HttpGet("register")]
    public async Task<ActionResult<ReportPage<SalesRegisterRow>>> GetRegister(
        [FromQuery, StringLength(100)] string? search, [FromQuery] int? customerId, [FromQuery] int? warehouseId,
        [FromQuery] string? saleType, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery, Range(1, 1000)] int limit = 50, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await reports.GetRegisterAsync(OrganizationId, new SalesReportQuery
        {
            Search = search,
            CustomerId = customerId,
            WarehouseId = warehouseId,
            SaleType = saleType,
            Limit = limit,
            Offset = offset
        }, from, to, cancellationToken));

    [HttpGet("by-product")]
    public async Task<ActionResult<ReportPage<SalesByProductRow>>> GetByProduct(
        [FromQuery, StringLength(100)] string? search, [FromQuery] int? customerId, [FromQuery] int? warehouseId,
        [FromQuery] int? categoryId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery, Range(1, 1000)] int limit = 50, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await reports.GetByProductAsync(OrganizationId, new SalesReportQuery
        {
            Search = search,
            CustomerId = customerId,
            WarehouseId = warehouseId,
            Limit = limit,
            Offset = offset
        }, from, to, categoryId, cancellationToken));
}
