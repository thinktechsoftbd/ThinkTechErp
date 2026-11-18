using Dapper;
using ThinkTechErpDomain.Models.Dashboard;
using ThinkTechErpRepository.Database;

namespace ThinkTechErpRepository.Dashboard;

/// <summary>A stretch of days and how its series is grouped ("day" or "month").</summary>
public sealed record DashboardRange(DateOnly From, DateOnly To, string Unit);

public interface IDashboardRepository
{
    /// <summary>Posted sales in <paramref name="current"/> (total, count, series, by category) and the previous total.</summary>
    Task<DashboardSales> GetSalesAsync(int organizationId, DashboardRange current, DashboardRange previousTotal,
        DashboardRange previousSeries, CancellationToken cancellationToken = default);

    Task<DashboardPurchase> GetPurchaseAsync(int organizationId, DashboardRange current, DashboardRange previousTotal,
        CancellationToken cancellationToken = default);

    Task<DashboardWarranty> GetWarrantyAsync(int organizationId, DashboardRange current, DashboardRange previousTotal,
        DateOnly today, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DashboardTransaction>> GetRecentTransactionsAsync(int organizationId, bool sales, bool purchase,
        bool warranty, int limit, CancellationToken cancellationToken = default);
}

public sealed class DashboardRepository(IDbConnectionFactory connectionFactory) : IDashboardRepository
{
    private const int CategoryLimit = 20;
    private const int ExpiringDays = 60;
    private const int ExpiringLimit = 5;

    public async Task<DashboardSales> GetSalesAsync(int organizationId, DashboardRange current, DashboardRange previousTotal,
        DashboardRange previousSeries, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT coalesce(sum(round(i.total_amount * i.exchange_rate, 4)), 0) AS Total, count(*)::int AS InvoiceCount
            FROM public.sales_invoice i
            WHERE i.organization_id = @Org AND i.status = 'Posted' AND i.invoice_date BETWEEN @From AND @To;

            SELECT coalesce(sum(round(i.total_amount * i.exchange_rate, 4)), 0)
            FROM public.sales_invoice i
            WHERE i.organization_id = @Org AND i.status = 'Posted' AND i.invoice_date BETWEEN @PrevFrom AND @PrevTo;

            {SeriesSql("public.sales_invoice", "@From", "@To")};

            {SeriesSql("public.sales_invoice", "@SeriesFrom", "@SeriesTo")};

            SELECT coalesce(c.category_name, 'Uncategorized') AS Name,
                   sum(round(l.line_total * i.exchange_rate, 4)) AS Amount
            FROM public.sales_invoice i
            JOIN public.sales_invoice_line l ON l.sales_invoice_id = i.sales_invoice_id
            LEFT JOIN public.product p ON p.product_id = l.product_id
            LEFT JOIN public.category c ON c.category_id = p.category_id
            WHERE i.organization_id = @Org AND i.status = 'Posted' AND i.invoice_date BETWEEN @From AND @To
            GROUP BY 1
            HAVING sum(l.line_total) <> 0
            ORDER BY 2 DESC
            LIMIT {CategoryLimit};
            """, new
        {
            Org = organizationId,
            current.From,
            current.To,
            current.Unit,
            PrevFrom = previousTotal.From,
            PrevTo = previousTotal.To,
            SeriesFrom = previousSeries.From,
            SeriesTo = previousSeries.To
        }, cancellationToken: cancellationToken));

        var sales = await results.ReadSingleAsync<DashboardSales>();
        sales.PreviousTotal = await results.ReadSingleAsync<decimal>();
        sales.Series = (await results.ReadAsync<DashboardPoint>()).AsList();
        sales.PreviousSeries = (await results.ReadAsync<DashboardPoint>()).AsList();
        sales.ByCategory = (await results.ReadAsync<DashboardShare>()).AsList();
        return sales;
    }

    public async Task<DashboardPurchase> GetPurchaseAsync(int organizationId, DashboardRange current, DashboardRange previousTotal,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT coalesce(sum(round(i.total_amount * i.exchange_rate, 4)), 0) AS Total, count(*)::int AS InvoiceCount
            FROM public.purchase_invoice i
            WHERE i.organization_id = @Org AND i.status = 'Posted' AND i.invoice_date BETWEEN @From AND @To;

            SELECT coalesce(sum(round(i.total_amount * i.exchange_rate, 4)), 0)
            FROM public.purchase_invoice i
            WHERE i.organization_id = @Org AND i.status = 'Posted' AND i.invoice_date BETWEEN @PrevFrom AND @PrevTo;

            {SeriesSql("public.purchase_invoice", "@From", "@To")};
            """, new
        {
            Org = organizationId,
            current.From,
            current.To,
            current.Unit,
            PrevFrom = previousTotal.From,
            PrevTo = previousTotal.To
        }, cancellationToken: cancellationToken));

        var purchase = await results.ReadSingleAsync<DashboardPurchase>();
        purchase.PreviousTotal = await results.ReadSingleAsync<decimal>();
        purchase.Series = (await results.ReadAsync<DashboardPoint>()).AsList();
        return purchase;
    }

    public async Task<DashboardWarranty> GetWarrantyAsync(int organizationId, DashboardRange current, DashboardRange previousTotal,
        DateOnly today, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT count(*) FILTER (WHERE j.received_at::date BETWEEN @From AND @To)::int AS Claims,
                   count(*) FILTER (WHERE j.received_at::date BETWEEN @PrevFrom AND @PrevTo)::int AS PreviousClaims
            FROM public.service_job j
            WHERE j.organization_id = @Org AND j.job_type = 'Warranty' AND j.status <> 'Cancelled'
              AND j.received_at::date BETWEEN least(@From, @PrevFrom) AND greatest(@To, @PrevTo);

            SELECT count(*) FILTER (WHERE r.status = 'Active' AND r.start_date <= @Today AND r.end_date >= @Today)::int AS ActiveCount,
                   count(*) FILTER (WHERE r.status = 'Active' AND r.start_date <= @Today AND r.end_date >= @Today
                                    AND EXISTS (SELECT 1 FROM public.service_job j
                                                WHERE j.warranty_registration_id = r.warranty_registration_id
                                                  AND j.job_type = 'Warranty'
                                                  AND j.status NOT IN ('Delivered', 'Cancelled')))::int AS UnderClaim,
                   count(*) FILTER (WHERE r.status = 'Active' AND r.end_date < @Today)::int AS Expired,
                   -- Active at the end of the compared stretch: still active, or replaced since then.
                   count(*) FILTER (WHERE (r.status = 'Active' OR (r.status = 'Replaced' AND r.updated_at::date > @PrevTo))
                                    AND r.start_date <= @PrevTo AND r.end_date >= @PrevTo)::int AS PreviousActiveCount
            FROM public.warranty_registration r
            WHERE r.organization_id = @Org;

            SELECT r.warranty_registration_id AS WarrantyRegistrationId, r.warranty_no AS WarrantyNo,
                   coalesce(p.product_name, 'Product') AS ProductName, p.image_url AS ProductImageUrl,
                   r.serial_no AS SerialNo, r.customer_name AS CustomerName, r.end_date AS EndDate,
                   (r.end_date - @Today)::int AS DaysLeft
            FROM public.warranty_registration r
            LEFT JOIN public.product p ON p.product_id = r.product_id
            WHERE r.organization_id = @Org AND r.status = 'Active'
              AND r.end_date BETWEEN @Today AND @Today + {ExpiringDays}
            ORDER BY r.end_date, r.warranty_registration_id
            LIMIT {ExpiringLimit};
            """, new
        {
            Org = organizationId,
            current.From,
            current.To,
            PrevFrom = previousTotal.From,
            PrevTo = previousTotal.To,
            Today = today
        }, cancellationToken: cancellationToken));

        var claims = await results.ReadSingleAsync<(int Claims, int PreviousClaims)>();
        var warranty = await results.ReadSingleAsync<DashboardWarranty>();
        warranty.Claims = claims.Claims;
        warranty.PreviousClaims = claims.PreviousClaims;
        warranty.InWarranty = warranty.ActiveCount - warranty.UnderClaim;
        warranty.ExpiringSoon = (await results.ReadAsync<DashboardExpiringWarranty>()).AsList();
        return warranty;
    }

    public async Task<IReadOnlyList<DashboardTransaction>> GetRecentTransactionsAsync(int organizationId, bool sales,
        bool purchase, bool warranty, int limit, CancellationToken cancellationToken = default)
    {
        var parts = new List<string>();
        if (sales)
            parts.Add("""
                (SELECT 'Sale' AS Type, i.sales_invoice_id AS Id, i.invoice_date AS "Date", i.invoice_no AS ReferenceNo,
                        coalesce(i.customer_name, s.supplier_name) AS PartyName,
                        round(i.total_amount * i.exchange_rate, 4) AS Amount, i.status AS Status, i.created_at AS CreatedAt
                 FROM public.sales_invoice i
                 LEFT JOIN public.supplier s ON s.supplier_id = i.customer_id
                 WHERE i.organization_id = @Org
                 ORDER BY i.invoice_date DESC, i.created_at DESC
                 LIMIT @Limit)
                """);
        if (purchase)
            parts.Add("""
                (SELECT 'Purchase', i.purchase_invoice_id, i.invoice_date, i.invoice_no,
                        coalesce(i.supplier_name, s.supplier_name),
                        round(i.total_amount * i.exchange_rate, 4), i.status, i.created_at
                 FROM public.purchase_invoice i
                 LEFT JOIN public.supplier s ON s.supplier_id = i.supplier_id
                 WHERE i.organization_id = @Org
                 ORDER BY i.invoice_date DESC, i.created_at DESC
                 LIMIT @Limit)
                """);
        if (warranty)
            parts.Add("""
                (SELECT 'Warranty', j.service_job_id, j.received_at::date, j.job_no, j.customer_name,
                        NULL::numeric, j.status, j.created_at
                 FROM public.service_job j
                 WHERE j.organization_id = @Org AND j.job_type = 'Warranty'
                 ORDER BY j.received_at DESC, j.created_at DESC
                 LIMIT @Limit)
                """);
        if (parts.Count == 0)
            return [];

        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<DashboardTransaction>(new CommandDefinition($"""
            SELECT Type, Id, "Date", ReferenceNo, PartyName, Amount, Status
            FROM ({string.Join("\nUNION ALL\n", parts)}) t
            ORDER BY "Date" DESC, CreatedAt DESC
            LIMIT @Limit
            """, new { Org = organizationId, Limit = limit }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    /// <summary>Posted documents' amounts per day or month (@Unit) between two date parameters.</summary>
    private static string SeriesSql(string table, string from, string to) => $"""
        SELECT date_trunc(@Unit, i.invoice_date)::date AS "Date", sum(round(i.total_amount * i.exchange_rate, 4)) AS Amount
        FROM {table} i
        WHERE i.organization_id = @Org AND i.status = 'Posted' AND i.invoice_date BETWEEN {from} AND {to}
        GROUP BY 1
        ORDER BY 1
        """;
}
