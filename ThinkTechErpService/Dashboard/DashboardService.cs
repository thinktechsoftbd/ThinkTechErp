using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Dashboard;
using ThinkTechErpRepository.Dashboard;

namespace ThinkTechErpService.Dashboard;

/// <summary>Which dashboard sections the user may see (from SALES.V, PURCHASE.V and WARRANTY.V).</summary>
public sealed record DashboardAccess(bool Sales, bool Purchase, bool Warranty);

public interface IDashboardService
{
    /// <param name="today">Today in the organization's time zone (the client knows it); null = today in UTC.</param>
    Task<DashboardOverview> GetOverviewAsync(int organizationId, string? period, DateOnly? today, DashboardAccess access,
        CancellationToken cancellationToken = default);
}

public sealed class DashboardService(IDashboardRepository dashboard) : IDashboardService
{
    private const int RecentTransactionLimit = 8;

    public async Task<DashboardOverview> GetOverviewAsync(int organizationId, string? period, DateOnly? today,
        DashboardAccess access, CancellationToken cancellationToken = default)
    {
        period = string.IsNullOrWhiteSpace(period) ? DashboardPeriods.ThisMonth : period.Trim().ToLowerInvariant();
        if (!DashboardPeriods.All.Contains(period))
            throw new BusinessRuleException($"Unknown period \"{period}\". Use {string.Join(", ", DashboardPeriods.All)}.");

        var day = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var overview = Periods(period, day);
        var unit = overview.Granularity;
        var current = new DashboardRange(overview.From, overview.To, unit);
        var previousTotal = new DashboardRange(overview.PreviousFrom, overview.PreviousTo, unit);
        var previousSeries = new DashboardRange(overview.PreviousFrom, overview.PreviousAxisTo, unit);

        // One connection each, run together: the sections don't depend on each other.
        var sales = access.Sales ? dashboard.GetSalesAsync(organizationId, current, previousTotal, previousSeries, cancellationToken) : null;
        var purchase = access.Purchase ? dashboard.GetPurchaseAsync(organizationId, current, previousTotal, cancellationToken) : null;
        var warranty = access.Warranty ? dashboard.GetWarrantyAsync(organizationId, current, previousTotal, day, cancellationToken) : null;
        var recent = dashboard.GetRecentTransactionsAsync(organizationId, access.Sales, access.Purchase, access.Warranty,
            RecentTransactionLimit, cancellationToken);

        if (sales is not null)
        {
            overview.Sales = await sales;
            overview.Sales.Series = Fill(overview.Sales.Series, overview.From, overview.To, unit);
            overview.Sales.PreviousSeries = Fill(overview.Sales.PreviousSeries, overview.PreviousFrom, overview.PreviousAxisTo, unit);
        }
        if (purchase is not null)
        {
            overview.Purchase = await purchase;
            overview.Purchase.Series = Fill(overview.Purchase.Series, overview.From, overview.To, unit);
        }
        if (warranty is not null)
            overview.Warranty = await warranty;
        overview.RecentTransactions = await recent;
        return overview;
    }

    /// <summary>The period so far, its axis, and the same stretch of the previous period.</summary>
    private static DashboardOverview Periods(string period, DateOnly today)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        switch (period)
        {
            case DashboardPeriods.LastMonth:
            {
                var from = monthStart.AddMonths(-1);
                var to = monthStart.AddDays(-1);
                var previousFrom = from.AddMonths(-1);
                var previousTo = from.AddDays(-1);
                return new DashboardOverview
                {
                    Period = period, Today = today, Granularity = "day",
                    From = from, To = to, AxisTo = to,
                    PreviousFrom = previousFrom, PreviousTo = previousTo, PreviousAxisTo = previousTo
                };
            }
            case DashboardPeriods.ThisYear:
            {
                var from = new DateOnly(today.Year, 1, 1);
                var previousFrom = from.AddYears(-1);
                return new DashboardOverview
                {
                    Period = period, Today = today, Granularity = "month",
                    From = from, To = today, AxisTo = new DateOnly(today.Year, 12, 31),
                    // 1 January to the same day last year (28 February when today is 29 February).
                    PreviousFrom = previousFrom, PreviousTo = today.AddYears(-1),
                    PreviousAxisTo = new DateOnly(today.Year - 1, 12, 31)
                };
            }
            default:
            {
                var previousFrom = monthStart.AddMonths(-1);
                var previousEnd = monthStart.AddDays(-1);
                // The same days last month: 1–18 October for 1–18 November (1–30 for 1–31).
                var previousTo = previousFrom.AddDays(Math.Min(today.Day, previousEnd.Day) - 1);
                return new DashboardOverview
                {
                    Period = period, Today = today, Granularity = "day",
                    From = monthStart, To = today, AxisTo = monthStart.AddMonths(1).AddDays(-1),
                    PreviousFrom = previousFrom, PreviousTo = previousTo, PreviousAxisTo = previousEnd
                };
            }
        }
    }

    /// <summary>Every day (or month) from <paramref name="from"/> to <paramref name="to"/>, zero where nothing was found.</summary>
    private static List<DashboardPoint> Fill(IReadOnlyList<DashboardPoint> points, DateOnly from, DateOnly to, string unit)
    {
        var amounts = points.ToDictionary(point => point.Date, point => point.Amount);
        var filled = new List<DashboardPoint>();
        for (var date = unit == "month" ? new DateOnly(from.Year, from.Month, 1) : from;
             date <= to;
             date = unit == "month" ? date.AddMonths(1) : date.AddDays(1))
        {
            filled.Add(new DashboardPoint { Date = date, Amount = amounts.GetValueOrDefault(date) });
        }
        return filled;
    }
}
