using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Accounting;
using ThinkTechErpService.Accounting;

namespace ThinkTechErpAPI.Areas.Accounting.Controller;

/// <summary>
/// The chart of accounts, five levels deep: Parent Class → Account Class → Main Class → Map Class → Sub Class.
/// Sub classes are the ledger heads vouchers post to.
/// </summary>
[Route("api/accounting/chart-of-accounts")]
[Authorize(Policy = AccountingPolicies.View)]
public sealed class AccountingChartOfAccountsController(IChartOfAccountsService chart) : OrganizationControllerBase
{
    /// <summary>Every account, every level, as a flat list; ParentId links it into a tree.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ChartAccount>>> GetAccounts(CancellationToken cancellationToken)
        => Ok(await chart.GetAccountsAsync(OrganizationId, cancellationToken));

    [HttpGet("{accountId:int}")]
    public async Task<ActionResult<ChartAccount>> GetAccount(int accountId, CancellationToken cancellationToken)
        => OkOrNotFound(await chart.GetAccountAsync(OrganizationId, accountId, cancellationToken));

    /// <summary>
    /// Sub classes for the voucher and report pickers (by code or name). ledgerTypes (comma separated: General,
    /// Cash, Bank) and partyType (Supplier, Customer) narrow them; with ids it returns exactly those heads.
    /// </summary>
    [HttpGet("ledger-heads")]
    public async Task<ActionResult<IReadOnlyList<LedgerHeadOption>>> GetLedgerHeads(
        [FromQuery, StringLength(100)] string? search, [FromQuery, StringLength(60)] string? ledgerTypes,
        [FromQuery, StringLength(20)] string? partyType, [FromQuery, StringLength(500)] string? ids,
        [FromQuery] bool includeInactive = false, [FromQuery, Range(1, 500)] int limit = 30,
        CancellationToken cancellationToken = default)
        => Ok(await chart.GetLedgerHeadsAsync(OrganizationId, search,
            ledgerTypes?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            partyType, Ids.Parse(ids), includeInactive, limit, cancellationToken));

    /// <summary>Adds an account under ParentId (none for a parent class); an empty code takes the next one.</summary>
    [HttpPost]
    [Authorize(Policy = AccountingPolicies.Manage)]
    public async Task<ActionResult<ChartAccount>> CreateAccount([FromBody] ChartAccountUpsertRequest request, CancellationToken cancellationToken)
    {
        var account = await chart.CreateAccountAsync(OrganizationId, request, UserId, cancellationToken);
        return CreatedAtAction(nameof(GetAccount), new { accountId = account.Id }, account);
    }

    [HttpPut("{accountId:int}")]
    [Authorize(Policy = AccountingPolicies.Manage)]
    public async Task<ActionResult<ChartAccount>> UpdateAccount(int accountId, [FromBody] ChartAccountUpsertRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await chart.UpdateAccountAsync(OrganizationId, accountId, request, UserId, cancellationToken));

    /// <summary>The tree's checkbox: ticking activates the branch and its parents, clearing deactivates the branch.</summary>
    [HttpPut("{accountId:int}/active")]
    [Authorize(Policy = AccountingPolicies.Manage)]
    public async Task<ActionResult<ChartAccount>> SetActive(int accountId, [FromBody] ChartAccountActiveRequest request, CancellationToken cancellationToken)
        => OkOrNotFound(await chart.SetActiveAsync(OrganizationId, accountId, request.IsActive, UserId, cancellationToken));

    [HttpDelete("{accountId:int}")]
    [Authorize(Policy = AccountingPolicies.Manage)]
    public async Task<IActionResult> DeleteAccount(int accountId, CancellationToken cancellationToken)
        => DeleteResult(await chart.DeleteAccountAsync(OrganizationId, accountId, cancellationToken),
            "The account has accounts below it or voucher entries. Clear its checkbox to stop using it instead.");

    /// <summary>Adds a ready-made chart for a trading business; only when the organization has no accounts yet.</summary>
    [HttpPost("standard")]
    [Authorize(Policy = AccountingPolicies.Manage)]
    public async Task<ActionResult<IReadOnlyList<ChartAccount>>> ImportStandardChart(CancellationToken cancellationToken)
        => Ok(await chart.ImportStandardChartAsync(OrganizationId, UserId, cancellationToken));
}

/// <summary>Suppliers and customers for the subsidiary-ledger pickers, so accountants need no Purchase or Sales access.</summary>
[Route("api/accounting/parties")]
[Authorize(Policy = AccountingPolicies.View)]
public sealed class AccountingPartiesController(IChartOfAccountsService chart) : OrganizationControllerBase
{
    /// <param name="type">Supplier or Customer.</param>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PartyOption>>> GetParties(
        [FromQuery, Required, StringLength(20)] string type, [FromQuery, StringLength(100)] string? search,
        [FromQuery, StringLength(500)] string? ids, [FromQuery, Range(1, 500)] int limit = 20,
        CancellationToken cancellationToken = default)
        => Ok(await chart.GetPartiesAsync(OrganizationId, type, search, Ids.Parse(ids), limit, cancellationToken));
}

internal static class Ids
{
    /// <summary>"3,5,8" → [3, 5, 8]; at most 100, ignoring anything that isn't an id.</summary>
    public static IReadOnlyCollection<int>? Parse(string? ids)
        => ids?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => int.TryParse(id, out var value) ? value : 0).Where(id => id > 0).Distinct().Take(100).ToList();
}
