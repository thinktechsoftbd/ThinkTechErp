using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Accounting;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpService.Accounting;

namespace ThinkTechErpAPI.Areas.Accounting.Controller;

/// <summary>
/// Sub classes of the chart of accounts (accounting.acc_sub_class): the ledger heads vouchers post to.
/// The chart itself is kept under api/accounting/account-classes.
/// </summary>
[Route("api/accounting/ledger-heads")]
[Authorize(Policy = AccountingPolicies.View)]
public sealed class AccountingLedgerHeadsController(ILedgerLookupService lookups) : ErpControllerBase
{
    /// <summary>Sub classes for the voucher and report pickers (by code or name); with ids it returns exactly those heads.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LedgerHeadOption>>> GetLedgerHeads(
        [FromQuery, StringLength(100)] string? search, [FromQuery, StringLength(500)] string? ids,
        [FromQuery, Range(1, 500)] int limit = 30, CancellationToken cancellationToken = default)
        => Ok(await lookups.GetLedgerHeadsAsync(search, Ids.Parse(ids), limit, cancellationToken));

    /// <summary>
    /// A page of sub classes for the search-as-you-type pickers, by sub code or sub name; each with its
    /// map/main/account/parent class path.
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<PagedResult<LedgerHeadOption>>> SearchLedgerHeads(
        [FromQuery, StringLength(100)] string? search, [FromQuery, Range(1, 100)] int limit = 20,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0, CancellationToken cancellationToken = default)
        => Ok(await lookups.SearchLedgerHeadsAsync(search, limit, offset, cancellationToken));
}

/// <summary>Suppliers and customers for the voucher and report pickers, so accountants need no Purchase or Sales access.</summary>
[Route("api/accounting/parties")]
[Authorize(Policy = AccountingPolicies.View)]
public sealed class AccountingPartiesController(ILedgerLookupService lookups) : OrganizationControllerBase
{
    /// <param name="type">Supplier or Customer; empty lists both.</param>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PartyOption>>> GetParties(
        [FromQuery, StringLength(20)] string? type, [FromQuery, StringLength(100)] string? search,
        [FromQuery, StringLength(500)] string? ids, [FromQuery, Range(1, 500)] int limit = 20,
        CancellationToken cancellationToken = default)
        => Ok(await lookups.GetPartiesAsync(OrganizationId, type, search, Ids.Parse(ids), limit, cancellationToken));
}

internal static class Ids
{
    /// <summary>"3,5,8" → [3, 5, 8]; at most 100, ignoring anything that isn't an id.</summary>
    public static IReadOnlyCollection<int>? Parse(string? ids)
        => ids?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => int.TryParse(id, out var value) ? value : 0).Where(id => id > 0).Distinct().Take(100).ToList();
}
