using ThinkTechErpDomain.Models.Accounting;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpRepository.Accounting;
using ThinkTechErpRepository.Common;
using ThinkTechErpService.Inventory;

namespace ThinkTechErpService.Accounting;

public interface IAccountingReportService
{
    /// <param name="partyId">A supplier or customer, to narrow the ledger to that party.</param>
    Task<SubsidiaryLedger?> GetSubsidiaryLedgerAsync(
        int organizationId, int accountId, int? partyId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);
    Task<TrialBalance> GetTrialBalanceAsync(int organizationId, DateOnly? from, DateOnly? to, bool includeZero, CancellationToken cancellationToken = default);
    Task<BalanceSheet> GetBalanceSheetAsync(int organizationId, DateOnly? asOf, CancellationToken cancellationToken = default);
    Task<PartyBalance> GetPartyBalanceAsync(
        int organizationId, int accountId, int partyId, DateOnly? asOf, CancellationToken cancellationToken = default);
}

public sealed class AccountingReportService(IAccountingReportRepository reports, IOwnershipRepository ownership) : IAccountingReportService
{
    /// <summary>Before any voucher: the balance sheet adds up everything posted up to its date.</summary>
    private static readonly DateOnly Beginning = new(1900, 1, 1);

    public async Task<SubsidiaryLedger?> GetSubsidiaryLedgerAsync(
        int organizationId, int accountId, int? partyId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var (start, end) = InventoryReportService.Period(from, to);
        await ownership.EnsureAsync(organizationId, "party", new[] { partyId }, cancellationToken);
        return await reports.GetSubsidiaryLedgerAsync(organizationId, accountId, partyId, start, end, cancellationToken);
    }

    public async Task<TrialBalance> GetTrialBalanceAsync(int organizationId, DateOnly? from, DateOnly? to, bool includeZero, CancellationToken cancellationToken = default)
    {
        var (start, end) = InventoryReportService.Period(from, to);
        var rows = await reports.GetBalancesAsync(organizationId, start, end, includeZero, cancellationToken);
        return new TrialBalance
        {
            From = start,
            To = end,
            Rows = rows,
            OpeningDebit = rows.Sum(r => Math.Max(r.OpeningBalance, 0)),
            OpeningCredit = rows.Sum(r => Math.Max(-r.OpeningBalance, 0)),
            PeriodDebit = rows.Sum(r => r.PeriodDebit),
            PeriodCredit = rows.Sum(r => r.PeriodCredit),
            ClosingDebit = rows.Sum(r => Math.Max(r.ClosingBalance, 0)),
            ClosingCredit = rows.Sum(r => Math.Max(-r.ClosingBalance, 0))
        };
    }

    public async Task<BalanceSheet> GetBalanceSheetAsync(int organizationId, DateOnly? asOf, CancellationToken cancellationToken = default)
    {
        var date = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var rows = (await reports.GetBalancesAsync(organizationId, Beginning, date, false, cancellationToken))
            .Where(r => AccountNature.IsBalanceSheet(r.Nature) && r.ClosingBalance != 0)
            .ToList();
        var netProfit = await reports.GetNetProfitAsync(organizationId, date, cancellationToken);
        return new BalanceSheet
        {
            AsOf = date,
            Rows = rows,
            NetProfit = netProfit,
            TotalAssets = rows.Where(r => r.Nature == AccountNature.Asset).Sum(r => r.ClosingBalance),
            TotalLiabilities = -rows.Where(r => r.Nature == AccountNature.Liability).Sum(r => r.ClosingBalance),
            TotalEquity = -rows.Where(r => r.Nature == AccountNature.Equity).Sum(r => r.ClosingBalance) + netProfit
        };
    }

    public async Task<PartyBalance> GetPartyBalanceAsync(
        int organizationId, int accountId, int partyId, DateOnly? asOf, CancellationToken cancellationToken = default)
    {
        await ownership.EnsureAsync(organizationId, "party", new int?[] { partyId }, cancellationToken);
        return new PartyBalance
        {
            AccountId = accountId,
            PartyId = partyId,
            Balance = await reports.GetPartyBalanceAsync(organizationId, accountId, partyId,
                asOf ?? DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken)
        };
    }
}
