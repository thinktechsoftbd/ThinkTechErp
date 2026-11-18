namespace ThinkTechErpDomain.Models.Dashboard;

/// <summary>The dashboard periods: a period and the one before it, which the "vs" figures compare with.</summary>
public static class DashboardPeriods
{
    public const string ThisMonth = "this-month";
    public const string LastMonth = "last-month";
    public const string ThisYear = "this-year";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        ThisMonth, LastMonth, ThisYear
    };
}

/// <summary>
/// The dashboard of one organization for a period: sales, purchases and warranty, each only when the user may
/// see that module (else null). Amounts are in the base currency (document amount × its exchange rate) and count
/// posted documents only.
/// </summary>
public sealed class DashboardOverview
{
    public string Period { get; set; } = DashboardPeriods.ThisMonth;

    /// <summary>The period so far: totals and the series cover From..To (To is today in a current period).</summary>
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }

    /// <summary>The end of the period's chart axis: the month's last day, or 31 December.</summary>
    public DateOnly AxisTo { get; set; }

    /// <summary>
    /// The same stretch of the previous period, which the totals compare with (e.g. 1–18 October for 1–18
    /// November). The previous series covers the whole previous period.
    /// </summary>
    public DateOnly PreviousFrom { get; set; }
    public DateOnly PreviousTo { get; set; }
    public DateOnly PreviousAxisTo { get; set; }

    public DateOnly Today { get; set; }

    /// <summary>"day" (a point per day, month periods) or "month" (a point per month, the year).</summary>
    public string Granularity { get; set; } = "day";

    public DashboardSales? Sales { get; set; }
    public DashboardPurchase? Purchase { get; set; }
    public DashboardWarranty? Warranty { get; set; }

    /// <summary>The latest sales, purchases and warranty claims the user may see, newest first.</summary>
    public IReadOnlyList<DashboardTransaction> RecentTransactions { get; set; } = [];
}

/// <summary>An amount for one day, or for the month starting on <see cref="Date"/>.</summary>
public sealed class DashboardPoint
{
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
}

public sealed class DashboardSales
{
    public decimal Total { get; set; }
    public decimal PreviousTotal { get; set; }
    public int InvoiceCount { get; set; }
    /// <summary>Every day (or month) of the period, zero where nothing was sold.</summary>
    public IReadOnlyList<DashboardPoint> Series { get; set; } = [];
    /// <summary>The previous period, point by point (its first day lines up with the period's first day).</summary>
    public IReadOnlyList<DashboardPoint> PreviousSeries { get; set; } = [];
    /// <summary>Sales by product category in the period, largest first.</summary>
    public IReadOnlyList<DashboardShare> ByCategory { get; set; } = [];
}

public sealed class DashboardPurchase
{
    public decimal Total { get; set; }
    public decimal PreviousTotal { get; set; }
    public int InvoiceCount { get; set; }
    public IReadOnlyList<DashboardPoint> Series { get; set; } = [];
}

public sealed class DashboardShare
{
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public sealed class DashboardWarranty
{
    /// <summary>Warranty claims (service jobs of type Warranty) received in the period, and in the one before.</summary>
    public int Claims { get; set; }
    public int PreviousClaims { get; set; }

    /// <summary>Active warranty cards not yet expired today, and at the end of the previous period.</summary>
    public int ActiveCount { get; set; }
    public int PreviousActiveCount { get; set; }

    /// <summary>Of the active cards today: covered with no open claim, covered with an open claim, expired.</summary>
    public int InWarranty { get; set; }
    public int UnderClaim { get; set; }
    public int Expired { get; set; }

    /// <summary>Active cards that end within the next 60 days, soonest first.</summary>
    public IReadOnlyList<DashboardExpiringWarranty> ExpiringSoon { get; set; } = [];
}

public sealed class DashboardExpiringWarranty
{
    public long WarrantyRegistrationId { get; set; }
    public string WarrantyNo { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? ProductImageUrl { get; set; }
    public string? SerialNo { get; set; }
    public string? CustomerName { get; set; }
    public DateOnly EndDate { get; set; }
    public int DaysLeft { get; set; }
}

public sealed class DashboardTransaction
{
    /// <summary>Sale, Purchase or Warranty.</summary>
    public string Type { get; set; } = string.Empty;
    public long Id { get; set; }
    public DateOnly Date { get; set; }
    public string ReferenceNo { get; set; } = string.Empty;
    public string? PartyName { get; set; }
    /// <summary>In the base currency; null for a warranty claim.</summary>
    public decimal? Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}
