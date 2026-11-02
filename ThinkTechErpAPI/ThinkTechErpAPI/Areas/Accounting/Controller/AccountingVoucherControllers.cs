using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Accounting;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpService.Accounting;

namespace ThinkTechErpAPI.Areas.Accounting.Controller;

/// <summary>Query string shared by the voucher lists.</summary>
public sealed class VoucherListQuery
{
    [StringLength(100)] public string? Search { get; set; }
    /// <summary>A "Voucher Type" lookup value.</summary>
    [Range(1, long.MaxValue)] public long? VoucherTypeId { get; set; }
    [StringLength(20)] public string? Status { get; set; }
    /// <summary>A supplier or customer named on any line.</summary>
    [Range(1, int.MaxValue)] public int? PartyId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    [Range(1, 500)] public int Limit { get; set; } = 20;
    [Range(0, int.MaxValue)] public int Offset { get; set; }

    public VoucherQuery ToQuery() => new()
    {
        Search = Search,
        VoucherTypeId = VoucherTypeId,
        Status = Status,
        PartyId = PartyId,
        From = From,
        To = To,
        Limit = Limit,
        Offset = Offset
    };
}

/// <summary>
/// Vouchers of every type on the "Voucher Type" lookup list, numbered 00000000001, ... per organization.
/// Saved as drafts, posted when debits equal credits, and cancelled (never edited) once posted. A deleted
/// draft is only flagged (is_deleted). The list shows bill payments and collections too.
/// </summary>
[Route("api/accounting/vouchers")]
[Authorize(Policy = AccountingPolicies.View)]
public sealed class AccountingVouchersController(IVoucherService vouchers) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<Voucher>>> GetVouchers([FromQuery] VoucherListQuery query, CancellationToken cancellationToken)
        => Ok(await vouchers.GetVouchersAsync(OrganizationId, query.ToQuery(), cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<Voucher>> GetVoucher(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await vouchers.GetVoucherAsync(OrganizationId, id, cancellationToken));

    /// <summary>The "Voucher Type" lookup list (Settings → Lookup Data), inactive values included.</summary>
    [HttpGet("types")]
    public async Task<ActionResult<IReadOnlyList<LookupValueOption>>> GetVoucherTypes(CancellationToken cancellationToken)
        => Ok(await vouchers.GetVoucherTypesAsync(cancellationToken));

    /// <summary>The "Payment Method" lookup list, inactive values included.</summary>
    [HttpGet("payment-methods")]
    public async Task<ActionResult<IReadOnlyList<LookupValueOption>>> GetPaymentMethods(CancellationToken cancellationToken)
        => Ok(await vouchers.GetPaymentMethodsAsync(cancellationToken));

    /// <summary>Saves a draft; with post=true it is posted in the same call.</summary>
    [HttpPost]
    [Authorize(Policy = AccountingPolicies.Manage)]
    public async Task<ActionResult<Voucher>> CreateVoucher(
        [FromBody] VoucherUpsertRequest request, [FromQuery] bool post = false, CancellationToken cancellationToken = default)
    {
        var voucher = await vouchers.CreateVoucherAsync(OrganizationId, request, post, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetVoucher), new { id = voucher.Id }, voucher);
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = AccountingPolicies.Manage)]
    public async Task<ActionResult<Voucher>> UpdateVoucher(
        long id, [FromBody] VoucherUpsertRequest request, [FromQuery] bool post = false, CancellationToken cancellationToken = default)
        => OkOrNotFound(await vouchers.UpdateVoucherAsync(OrganizationId, id, request, post, UserId, cancellationToken));

    [HttpDelete("{id:long}")]
    [Authorize(Policy = AccountingPolicies.Manage)]
    public async Task<IActionResult> DeleteVoucher(long id, CancellationToken cancellationToken)
        => DeleteResult(await vouchers.DeleteVoucherAsync(OrganizationId, id, UserId, cancellationToken),
            "Only a draft voucher can be deleted; cancel a posted one instead.");

    [HttpPost("{id:long}/post")]
    [Authorize(Policy = AccountingPolicies.Manage)]
    public async Task<ActionResult<Voucher>> PostVoucher(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await vouchers.PostVoucherAsync(OrganizationId, id, UserId, cancellationToken));

    /// <summary>Takes a posted voucher (bill payments and collections too) out of the ledgers.</summary>
    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = AccountingPolicies.Manage)]
    public async Task<ActionResult<Voucher>> CancelVoucher(long id, CancellationToken cancellationToken)
        => OkOrNotFound(await vouchers.CancelVoucherAsync(OrganizationId, id, UserId, cancellationToken));
}

/// <summary>
/// Supplier bill payments: Cash or Bank Payment Vouchers that debit the supplier's payable head and credit
/// cash or bank, posted in one step.
/// </summary>
[Route("api/accounting/bill-payments")]
[Authorize(Policy = AccountingPolicies.View)]
public sealed class AccountingBillPaymentsController(IVoucherService vouchers) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<Voucher>>> GetBillPayments([FromQuery] VoucherListQuery query, CancellationToken cancellationToken)
        => Ok(await vouchers.GetBillPaymentsAsync(OrganizationId, query.ToQuery(), cancellationToken));

    /// <param name="request">PartyId is the supplier; PartyAccountId the sub class their balance is on; CashAccountId the cash or bank sub class paid from.</param>
    [HttpPost]
    [Authorize(Policy = AccountingPolicies.Manage)]
    public async Task<ActionResult<Voucher>> CreateBillPayment([FromBody] BillVoucherRequest request, CancellationToken cancellationToken)
    {
        var voucher = await vouchers.CreateBillPaymentAsync(OrganizationId, request, UserId, cancellationToken);
        return Created($"/api/accounting/vouchers/{voucher.Id}", voucher);
    }
}

/// <summary>
/// Customer bill collections: Cash or Bank Receive Vouchers that debit cash or bank and credit the customer's
/// receivable head, posted in one step.
/// </summary>
[Route("api/accounting/bill-collections")]
[Authorize(Policy = AccountingPolicies.View)]
public sealed class AccountingBillCollectionsController(IVoucherService vouchers) : OrganizationControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<Voucher>>> GetBillCollections([FromQuery] VoucherListQuery query, CancellationToken cancellationToken)
        => Ok(await vouchers.GetBillCollectionsAsync(OrganizationId, query.ToQuery(), cancellationToken));

    /// <param name="request">PartyId is the customer; PartyAccountId the sub class their balance is on; CashAccountId the cash or bank sub class deposited to.</param>
    [HttpPost]
    [Authorize(Policy = AccountingPolicies.Manage)]
    public async Task<ActionResult<Voucher>> CreateBillCollection([FromBody] BillVoucherRequest request, CancellationToken cancellationToken)
    {
        var voucher = await vouchers.CreateBillCollectionAsync(OrganizationId, request, UserId, cancellationToken);
        return Created($"/api/accounting/vouchers/{voucher.Id}", voucher);
    }
}

/// <summary>Reports over posted vouchers. Balances are debit minus credit: + is a debit balance, - a credit balance.</summary>
[Route("api/accounting/reports")]
[Authorize(Policy = AccountingPolicies.View)]
public sealed class AccountingReportsController(IAccountingReportService reports) : OrganizationControllerBase
{
    /// <summary>A ledger head's entries in a period with a running balance; partyId (a supplier or customer) narrows it to one party.</summary>
    [HttpGet("subsidiary-ledger")]
    public async Task<ActionResult<SubsidiaryLedger>> GetSubsidiaryLedger(
        [FromQuery, Range(1, int.MaxValue)] int accountId, [FromQuery] int? partyId,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
        => OkOrNotFound(await reports.GetSubsidiaryLedgerAsync(OrganizationId, accountId, partyId, from, to, cancellationToken));

    /// <summary>Every ledger head's opening balance, movement in the period and closing balance.</summary>
    [HttpGet("trial-balance")]
    public async Task<ActionResult<TrialBalance>> GetTrialBalance(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] bool includeZero = false,
        CancellationToken cancellationToken = default)
        => Ok(await reports.GetTrialBalanceAsync(OrganizationId, from, to, includeZero, cancellationToken));

    /// <summary>Assets, liabilities and equity on a date, with income less expenses to date under equity.</summary>
    [HttpGet("balance-sheet")]
    public async Task<ActionResult<BalanceSheet>> GetBalanceSheet([FromQuery] DateOnly? asOf, CancellationToken cancellationToken)
        => Ok(await reports.GetBalanceSheetAsync(OrganizationId, asOf, cancellationToken));

    /// <summary>A supplier's or customer's balance on a ledger head, for the bill payment and collection screens.</summary>
    [HttpGet("party-balance")]
    public async Task<ActionResult<PartyBalance>> GetPartyBalance(
        [FromQuery, Range(1, int.MaxValue)] int accountId, [FromQuery, Range(1, int.MaxValue)] int partyId,
        [FromQuery] DateOnly? asOf, CancellationToken cancellationToken)
        => Ok(await reports.GetPartyBalanceAsync(OrganizationId, accountId, partyId, asOf, cancellationToken));
}
