using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Inventory;
using ThinkTechErpDomain.Models.Purchase;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Purchase;
using ThinkTechErpService.Purchase;

namespace ThinkTechErpAPI.Areas.Purchase.Controller;

/// <summary>Query string shared by the purchase document lists.</summary>
public sealed class PurchaseListQuery
{
    [StringLength(100)] public string? Search { get; set; }
    public string? Status { get; set; }
    public int? SupplierId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? PurchaseType { get; set; }
    public string? PaymentStatus { get; set; }
    [Range(1, 500)] public int Limit { get; set; } = 20;
    [Range(0, int.MaxValue)] public int Offset { get; set; }

    public PurchaseDocumentQuery ToQuery() => new()
    {
        Search = Search,
        Status = Status,
        SupplierId = SupplierId,
        From = From,
        To = To,
        PurchaseType = PurchaseType,
        PaymentStatus = PaymentStatus,
        Limit = Limit,
        Offset = Offset
    };
}

/// <summary>
/// Supplier invoices: the usual way to enter a purchase (cash or credit). A direct invoice receives the
/// stock when posted; one with ReceivesStock = false bills earlier goods receipts.
/// </summary>
[Route("api/purchase/invoices")]
[Authorize(Policy = PurchasePolicies.View)]
public sealed class PurchaseInvoicesController(IPurchaseDocumentService documents) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<PurchaseInvoice>>> GetInvoices([FromQuery] PurchaseListQuery query, CancellationToken cancellationToken)
        => Ok(await documents.GetInvoicesAsync(OrganizationId, query.ToQuery(), cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<PurchaseInvoice>> GetInvoice(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.GetInvoiceAsync(OrganizationId, id, cancellationToken));

    /// <summary>Saves a draft; with post=true it is posted in the same call.</summary>
    [HttpPost]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<PurchaseInvoice>> CreateInvoice(
        [FromBody] PurchaseInvoiceUpsertRequest request, [FromQuery] bool post = false, CancellationToken cancellationToken = default)
    {
        var invoice = await documents.CreateInvoiceAsync(OrganizationId, request, UserId, cancellationToken);
        if (post)
            invoice = (await documents.PostInvoiceAsync(OrganizationId, invoice.Id, UserId, cancellationToken))!;
        return CreatedAtAction(nameof(GetInvoice), new { id = invoice.Id }, invoice);
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<PurchaseInvoice>> UpdateInvoice(
        long id, [FromBody] PurchaseInvoiceUpsertRequest request, [FromQuery] bool post = false, CancellationToken cancellationToken = default)
    {
        var invoice = await documents.UpdateInvoiceAsync(OrganizationId, id, request, UserId, cancellationToken);
        if (invoice is not null && post)
            invoice = await documents.PostInvoiceAsync(OrganizationId, id, UserId, cancellationToken);
        return OkOrNotFound(invoice);
    }

    [HttpDelete("{id:long}")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<IActionResult> DeleteInvoice(long id, CancellationToken cancellationToken)
        => DeleteResult(await documents.DeleteInvoiceAsync(OrganizationId, id, cancellationToken),
            "Only a draft invoice can be deleted; cancel a posted one instead.");

    [HttpPost("{id:long}/post")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<PurchaseInvoice>> PostInvoice(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.PostInvoiceAsync(OrganizationId, id, UserId, cancellationToken));

    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<PurchaseInvoice>> CancelInvoice(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.CancelInvoiceAsync(OrganizationId, id, UserId, cancellationToken));
}

[Route("api/purchase/orders")]
[Authorize(Policy = PurchasePolicies.View)]
public sealed class PurchaseOrdersController(IPurchaseDocumentService documents) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<PurchaseOrder>>> GetOrders([FromQuery] PurchaseListQuery query, CancellationToken cancellationToken)
        => Ok(await documents.GetOrdersAsync(OrganizationId, query.ToQuery(), cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<PurchaseOrder>> GetOrder(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.GetOrderAsync(OrganizationId, id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<PurchaseOrder>> CreateOrder([FromBody] PurchaseOrderUpsertRequest request, CancellationToken cancellationToken)
    {
        var order = await documents.CreateOrderAsync(OrganizationId, request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<PurchaseOrder>> UpdateOrder(long id, [FromBody] PurchaseOrderUpsertRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.UpdateOrderAsync(OrganizationId, id, request, UserId, cancellationToken));

    [HttpDelete("{id:long}")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<IActionResult> DeleteOrder(long id, CancellationToken cancellationToken)
        => DeleteResult(await documents.DeleteOrderAsync(OrganizationId, id, cancellationToken),
            "Only a draft purchase order can be deleted; cancel or close it instead.");

    [HttpPost("{id:long}/approve")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<PurchaseOrder>> ApproveOrder(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.ChangeOrderStatusAsync(OrganizationId, id, "Approve", UserId, cancellationToken));

    [HttpPost("{id:long}/close")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<PurchaseOrder>> CloseOrder(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.ChangeOrderStatusAsync(OrganizationId, id, "Close", UserId, cancellationToken));

    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<PurchaseOrder>> CancelOrder(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.ChangeOrderStatusAsync(OrganizationId, id, "Cancel", UserId, cancellationToken));
}

/// <summary>Goods received before the bill (optional: most shops enter the supplier invoice directly).</summary>
[Route("api/purchase/goods-receipts")]
[Authorize(Policy = PurchasePolicies.View)]
public sealed class PurchaseGoodsReceiptsController(IPurchaseDocumentService documents) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<GoodsReceipt>>> GetReceipts([FromQuery] PurchaseListQuery query, CancellationToken cancellationToken)
        => Ok(await documents.GetReceiptsAsync(OrganizationId, query.ToQuery(), cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<GoodsReceipt>> GetReceipt(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.GetReceiptAsync(OrganizationId, id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<GoodsReceipt>> CreateReceipt(
        [FromBody] GoodsReceiptUpsertRequest request, [FromQuery] bool post = false, CancellationToken cancellationToken = default)
    {
        var receipt = await documents.CreateReceiptAsync(OrganizationId, request, UserId, cancellationToken);
        if (post)
            receipt = (await documents.PostReceiptAsync(OrganizationId, receipt.Id, UserId, cancellationToken))!;
        return CreatedAtAction(nameof(GetReceipt), new { id = receipt.Id }, receipt);
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<GoodsReceipt>> UpdateReceipt(
        long id, [FromBody] GoodsReceiptUpsertRequest request, [FromQuery] bool post = false, CancellationToken cancellationToken = default)
    {
        var receipt = await documents.UpdateReceiptAsync(OrganizationId, id, request, UserId, cancellationToken);
        if (receipt is not null && post)
            receipt = await documents.PostReceiptAsync(OrganizationId, id, UserId, cancellationToken);
        return OkOrNotFound(receipt);
    }

    [HttpDelete("{id:long}")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<IActionResult> DeleteReceipt(long id, CancellationToken cancellationToken)
        => DeleteResult(await documents.DeleteReceiptAsync(OrganizationId, id, cancellationToken),
            "Only a draft goods receipt can be deleted; cancel a posted one instead.");

    [HttpPost("{id:long}/post")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<GoodsReceipt>> PostReceipt(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.PostReceiptAsync(OrganizationId, id, UserId, cancellationToken));

    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<GoodsReceipt>> CancelReceipt(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.CancelReceiptAsync(OrganizationId, id, UserId, cancellationToken));
}

[Route("api/purchase/returns")]
[Authorize(Policy = PurchasePolicies.View)]
public sealed class PurchaseReturnsController(IPurchaseDocumentService documents) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<PurchaseReturn>>> GetReturns([FromQuery] PurchaseListQuery query, CancellationToken cancellationToken)
        => Ok(await documents.GetReturnsAsync(OrganizationId, query.ToQuery(), cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<PurchaseReturn>> GetReturn(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.GetReturnAsync(OrganizationId, id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<PurchaseReturn>> CreateReturn(
        [FromBody] PurchaseReturnUpsertRequest request, [FromQuery] bool post = false, CancellationToken cancellationToken = default)
    {
        var purchaseReturn = await documents.CreateReturnAsync(OrganizationId, request, UserId, cancellationToken);
        if (post)
            purchaseReturn = (await documents.PostReturnAsync(OrganizationId, purchaseReturn.Id, UserId, cancellationToken))!;
        return CreatedAtAction(nameof(GetReturn), new { id = purchaseReturn.Id }, purchaseReturn);
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<PurchaseReturn>> UpdateReturn(
        long id, [FromBody] PurchaseReturnUpsertRequest request, [FromQuery] bool post = false, CancellationToken cancellationToken = default)
    {
        var purchaseReturn = await documents.UpdateReturnAsync(OrganizationId, id, request, UserId, cancellationToken);
        if (purchaseReturn is not null && post)
            purchaseReturn = await documents.PostReturnAsync(OrganizationId, id, UserId, cancellationToken);
        return OkOrNotFound(purchaseReturn);
    }

    [HttpDelete("{id:long}")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<IActionResult> DeleteReturn(long id, CancellationToken cancellationToken)
        => DeleteResult(await documents.DeleteReturnAsync(OrganizationId, id, cancellationToken),
            "Only a draft purchase return can be deleted; cancel a posted one instead.");

    [HttpPost("{id:long}/post")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<PurchaseReturn>> PostReturn(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.PostReturnAsync(OrganizationId, id, UserId, cancellationToken));

    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<PurchaseReturn>> CancelReturn(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.CancelReturnAsync(OrganizationId, id, UserId, cancellationToken));
}

/// <summary>Money paid to suppliers after the purchase, recorded and posted in one step.</summary>
[Route("api/purchase/payments")]
[Authorize(Policy = PurchasePolicies.View)]
public sealed class PurchasePaymentsController(IPurchaseDocumentService documents) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<SupplierPayment>>> GetPayments([FromQuery] PurchaseListQuery query, CancellationToken cancellationToken)
        => Ok(await documents.GetPaymentsAsync(OrganizationId, query.ToQuery(), cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<SupplierPayment>> GetPayment(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.GetPaymentAsync(OrganizationId, id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<SupplierPayment>> CreatePayment([FromBody] SupplierPaymentUpsertRequest request, CancellationToken cancellationToken)
    {
        var payment = await documents.CreatePaymentAsync(OrganizationId, request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetPayment), new { id = payment.Id }, payment);
    }

    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<SupplierPayment>> CancelPayment(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.CancelPaymentAsync(OrganizationId, id, UserId, cancellationToken));

    [HttpPut("{id:long}/cheque-status")]
    [Authorize(Policy = PurchasePolicies.Manage)]
    public async Task<ActionResult<SupplierPayment>> SetChequeStatus(long id, [FromBody] ChequeStatusRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await documents.SetChequeStatusAsync(OrganizationId, id, request.ChequeStatus, UserId, cancellationToken));
}

[Route("api/purchase/reports")]
[Authorize(Policy = PurchasePolicies.View)]
public sealed class PurchaseReportsController(IPurchaseReportService reports) : OrganizationControllerBase
{
    [HttpGet("supplier-ledger")]
    public async Task<ActionResult<SupplierLedger>> GetSupplierLedger(
        [FromQuery, Range(1, int.MaxValue)] int supplierId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
        => OkOrNotFound(await reports.GetSupplierLedgerAsync(OrganizationId, supplierId, from, to, cancellationToken));

    /// <summary>What is owed to each supplier, with unpaid invoices aged by days past due.</summary>
    [HttpGet("supplier-dues")]
    public async Task<ActionResult<ReportPage<SupplierDueRow>>> GetSupplierDues(
        [FromQuery, StringLength(100)] string? search, [FromQuery] DateOnly? asOf, [FromQuery] bool onlyWithBalance = true,
        [FromQuery, Range(1, 1000)] int limit = 50, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await reports.GetSupplierDuesAsync(OrganizationId, new PurchaseReportQuery
        {
            Search = search,
            OnlyWithBalance = onlyWithBalance,
            Limit = limit,
            Offset = offset
        }, asOf, cancellationToken));

    [HttpGet("register")]
    public async Task<ActionResult<ReportPage<PurchaseRegisterRow>>> GetRegister(
        [FromQuery, StringLength(100)] string? search, [FromQuery] int? supplierId, [FromQuery] string? purchaseType,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery, Range(1, 1000)] int limit = 50, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await reports.GetRegisterAsync(OrganizationId, new PurchaseReportQuery
        {
            Search = search,
            SupplierId = supplierId,
            PurchaseType = purchaseType,
            Limit = limit,
            Offset = offset
        }, from, to, cancellationToken));

    [HttpGet("by-product")]
    public async Task<ActionResult<ReportPage<PurchaseByProductRow>>> GetByProduct(
        [FromQuery, StringLength(100)] string? search, [FromQuery] int? supplierId, [FromQuery] int? categoryId,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery, Range(1, 1000)] int limit = 50, [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await reports.GetByProductAsync(OrganizationId, new PurchaseReportQuery
        {
            Search = search,
            SupplierId = supplierId,
            Limit = limit,
            Offset = offset
        }, from, to, categoryId, cancellationToken));
}
