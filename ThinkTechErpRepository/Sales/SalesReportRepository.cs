using Dapper;
using ThinkTechErpDomain.Models.Inventory;
using ThinkTechErpDomain.Models.Sales;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Sales;

public sealed class SalesReportQuery
{
    public string? Search { get; set; }
    public int? CustomerId { get; set; }
    public int? WarehouseId { get; set; }
    public string? SaleType { get; set; }
    public IReadOnlyCollection<int>? CategoryIds { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    /// <summary>Customer dues: leave out customers with nothing owed either way.</summary>
    public bool OnlyWithBalance { get; set; } = true;
    public int Limit { get; set; } = 50;
    public int Offset { get; set; }
}

public interface ISalesReportRepository
{
    Task<CustomerLedger?> GetCustomerLedgerAsync(int organizationId, int customerId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<ReportPage<CustomerDueRow>> GetCustomerDuesAsync(int organizationId, SalesReportQuery query, DateOnly asOf, CancellationToken cancellationToken = default);
    Task<ReportPage<SalesRegisterRow>> GetRegisterAsync(int organizationId, SalesReportQuery query, CancellationToken cancellationToken = default);
    Task<ReportPage<SalesByProductRow>> GetByProductAsync(int organizationId, SalesReportQuery query, CancellationToken cancellationToken = default);
}

public sealed class SalesReportRepository(IDbConnectionFactory connectionFactory) : ISalesReportRepository
{
    public async Task<CustomerLedger?> GetCustomerLedgerAsync(
        int organizationId, int customerId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        var ledger = await connection.QuerySingleOrDefaultAsync<CustomerLedger>(new CommandDefinition("""
            SELECT c.supplier_id AS CustomerId, c.supplier_code AS CustomerCode, c.supplier_name AS CustomerName,
                   c.mobile AS Mobile,
                   coalesce((SELECT sum(l.debit_amount - l.credit_amount) FROM sales.customer_ledger l
                             WHERE l.customer_id = c.supplier_id AND (l.entry_date IS NULL OR l.entry_date < @From)), 0) AS OpeningBalance
            FROM purchase.supplier c
            WHERE c.organization_id = @Org AND c.supplier_id = @CustomerId AND c.is_customer
            """, new { Org = organizationId, CustomerId = customerId, From = from }, cancellationToken: cancellationToken));
        if (ledger is null)
            return null;

        var entries = (await connection.QueryAsync<CustomerLedgerEntry>(new CommandDefinition("""
            SELECT l.entry_date AS EntryDate, l.entry_type AS EntryType, l.document_id AS DocumentId,
                   l.document_no AS DocumentNo, l.debit_amount AS DebitAmount, l.credit_amount AS CreditAmount
            FROM sales.customer_ledger l
            WHERE l.customer_id = @CustomerId AND l.entry_date BETWEEN @From AND @To
            ORDER BY l.entry_date, l.document_id, l.entry_type
            """, new { CustomerId = customerId, From = from, To = to }, cancellationToken: cancellationToken))).AsList();

        var balance = ledger.OpeningBalance;
        foreach (var entry in entries)
        {
            balance += entry.DebitAmount - entry.CreditAmount;
            entry.Balance = balance;
        }

        ledger.From = from;
        ledger.To = to;
        ledger.TotalDebit = entries.Sum(e => e.DebitAmount);
        ledger.TotalCredit = entries.Sum(e => e.CreditAmount);
        ledger.ClosingBalance = balance;
        ledger.Entries = entries;
        return ledger;
    }

    public async Task<ReportPage<CustomerDueRow>> GetCustomerDuesAsync(
        int organizationId, SalesReportQuery query, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var parameters = new
        {
            Org = organizationId,
            Pattern = RepositoryHelpers.Pattern(query.Search),
            query.OnlyWithBalance,
            AsOf = asOf,
            query.Limit,
            query.Offset
        };
        // Days overdue count from the due date (or the invoice date when there is none).
        const string rows = """
            WITH open_invoice AS (
                SELECT i.customer_id, (i.total_amount - i.settled_amount) * i.exchange_rate AS due,
                       coalesce(i.due_date, i.invoice_date) AS due_date,
                       @AsOf - coalesce(i.due_date, i.invoice_date) AS overdue_days
                FROM sales.sales_invoice i
                WHERE i.organization_id = @Org AND i.status = 'Posted' AND i.payment_status <> 'Paid' AND i.customer_id IS NOT NULL
            ),
            report AS (
                SELECT c.supplier_id AS CustomerId, c.supplier_code AS CustomerCode, c.supplier_name AS CustomerName,
                       c.mobile AS Mobile, c.customer_credit_limit AS CreditLimit,
                       coalesce((SELECT sum(l.debit_amount - l.credit_amount) FROM sales.customer_ledger l
                                 WHERE l.customer_id = c.supplier_id), 0) AS Balance,
                       (SELECT count(*) FROM open_invoice o WHERE o.customer_id = c.supplier_id)::int AS OpenInvoiceCount,
                       coalesce((SELECT round(sum(o.due), 4) FROM open_invoice o WHERE o.customer_id = c.supplier_id AND o.overdue_days <= 0), 0) AS NotDue,
                       coalesce((SELECT round(sum(o.due), 4) FROM open_invoice o WHERE o.customer_id = c.supplier_id AND o.overdue_days BETWEEN 1 AND 30), 0) AS Days1To30,
                       coalesce((SELECT round(sum(o.due), 4) FROM open_invoice o WHERE o.customer_id = c.supplier_id AND o.overdue_days BETWEEN 31 AND 60), 0) AS Days31To60,
                       coalesce((SELECT round(sum(o.due), 4) FROM open_invoice o WHERE o.customer_id = c.supplier_id AND o.overdue_days BETWEEN 61 AND 90), 0) AS Days61To90,
                       coalesce((SELECT round(sum(o.due), 4) FROM open_invoice o WHERE o.customer_id = c.supplier_id AND o.overdue_days > 90), 0) AS Over90,
                       (SELECT min(o.due_date) FROM open_invoice o WHERE o.customer_id = c.supplier_id) AS OldestDueDate
                FROM purchase.supplier c
                WHERE c.organization_id = @Org AND c.is_customer
                  AND (CAST(@Pattern AS text) IS NULL OR c.supplier_code ILIKE @Pattern OR c.supplier_name ILIKE @Pattern
                       OR c.mobile ILIKE @Pattern)
            ),
            filtered AS (SELECT * FROM report WHERE NOT @OnlyWithBalance OR Balance <> 0 OR OpenInvoiceCount > 0)
            """;
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            {rows} SELECT count(*), coalesce(sum(Balance), 0), coalesce(sum(Days1To30 + Days31To60 + Days61To90 + Over90), 0) FROM filtered;
            {rows} SELECT * FROM filtered ORDER BY Balance DESC, CustomerName LIMIT @Limit OFFSET @Offset;
            """, parameters, cancellationToken: cancellationToken));
        var (count, balance, overdue) = await results.ReadSingleAsync<(long, decimal, decimal)>();
        return new ReportPage<CustomerDueRow>
        {
            Items = (await results.ReadAsync<CustomerDueRow>()).AsList(),
            TotalCount = (int)count,
            Limit = query.Limit,
            Offset = query.Offset,
            Totals = new() { ["balance"] = balance, ["overdue"] = overdue }
        };
    }

    public async Task<ReportPage<SalesRegisterRow>> GetRegisterAsync(int organizationId, SalesReportQuery query, CancellationToken cancellationToken = default)
    {
        // Amounts in the base currency; profit = sales net of tax (and of the bill discount) - cost of goods sold.
        const string rows = """
            WITH report AS (
                SELECT i.sales_invoice_id AS InvoiceId, i.invoice_no AS InvoiceNo, i.invoice_date AS InvoiceDate,
                       i.sale_type AS SaleType, coalesce(c.supplier_name, i.customer_name) AS CustomerName,
                       coalesce(i.customer_mobile, c.mobile) AS CustomerMobile, w.warehouse_name AS WarehouseName,
                       cur.lookup_data_name AS Currency,
                       round(i.subtotal_amount * i.exchange_rate, 4) AS SubtotalAmount,
                       round(i.discount_amount * i.exchange_rate, 4) AS DiscountAmount,
                       round(i.tax_amount * i.exchange_rate, 4) AS TaxAmount,
                       round(i.total_amount * i.exchange_rate, 4) AS TotalAmount,
                       round(i.paid_amount * i.exchange_rate, 4) AS PaidAmount,
                       round(i.settled_amount * i.exchange_rate, 4) AS SettledAmount,
                       round((i.total_amount - i.settled_amount) * i.exchange_rate, 4) AS DueAmount,
                       coalesce((SELECT round(sum(l.unit_cost * l.base_quantity), 4) FROM sales.sales_invoice_line l
                                 WHERE l.sales_invoice_id = i.sales_invoice_id), 0) AS CostAmount,
                       i.payment_status AS PaymentStatus
                FROM sales.sales_invoice i
                LEFT JOIN purchase.supplier c ON c.supplier_id = i.customer_id
                JOIN inventory.warehouse w ON w.warehouse_id = i.warehouse_id
                JOIN public.lookup_data cur ON cur.lookup_data_id = i.currency_lookup_data_id
                WHERE i.organization_id = @Org AND i.status = 'Posted' AND i.invoice_date BETWEEN @From AND @To
                  AND (CAST(@CustomerId AS int) IS NULL OR i.customer_id = @CustomerId)
                  AND (CAST(@WarehouseId AS int) IS NULL OR i.warehouse_id = @WarehouseId)
                  AND (CAST(@SaleType AS text) IS NULL OR i.sale_type = @SaleType)
                  AND (CAST(@Pattern AS text) IS NULL OR i.invoice_no ILIKE @Pattern OR c.supplier_name ILIKE @Pattern
                       OR i.customer_name ILIKE @Pattern OR i.customer_mobile ILIKE @Pattern OR c.mobile ILIKE @Pattern)
            ),
            priced AS (SELECT *, TotalAmount - TaxAmount - CostAmount AS Profit FROM report)
            """;
        var parameters = new
        {
            Org = organizationId,
            query.From,
            query.To,
            query.CustomerId,
            query.WarehouseId,
            query.SaleType,
            Pattern = RepositoryHelpers.Pattern(query.Search),
            query.Limit,
            query.Offset
        };
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            {rows} SELECT count(*), coalesce(sum(SubtotalAmount), 0), coalesce(sum(DiscountAmount), 0), coalesce(sum(TaxAmount), 0),
                          coalesce(sum(TotalAmount), 0), coalesce(sum(PaidAmount), 0), coalesce(sum(DueAmount), 0),
                          coalesce(sum(CostAmount), 0), coalesce(sum(Profit), 0)
                   FROM priced;
            {rows} SELECT * FROM priced ORDER BY InvoiceDate, InvoiceId LIMIT @Limit OFFSET @Offset;
            -- Posted returns in the period: they take back sales (net of tax) and, for goods back in stock, the cost.
            SELECT coalesce(round(sum(r.total_amount * r.exchange_rate), 4), 0),
                   coalesce(round(sum(r.tax_amount * r.exchange_rate), 4), 0),
                   coalesce(round(sum((SELECT sum(l.unit_cost * l.base_quantity) FROM sales.sales_return_line l
                                       WHERE l.sales_return_id = r.sales_return_id AND l.is_restockable)), 4), 0)
            FROM sales.sales_return r
            LEFT JOIN sales.sales_invoice i ON i.sales_invoice_id = r.sales_invoice_id
            WHERE r.organization_id = @Org AND r.status = 'Posted' AND r.return_date BETWEEN @From AND @To
              AND (CAST(@CustomerId AS int) IS NULL OR r.customer_id = @CustomerId)
              AND (CAST(@WarehouseId AS int) IS NULL OR r.warehouse_id = @WarehouseId)
              AND (CAST(@SaleType AS text) IS NULL OR i.sale_type = @SaleType);
            """, parameters, cancellationToken: cancellationToken));
        var totals = await results.ReadSingleAsync<(long Count, decimal Subtotal, decimal Discount, decimal Tax, decimal Total,
            decimal Paid, decimal Due, decimal Cost, decimal Profit)>();
        var items = (await results.ReadAsync<SalesRegisterRow>()).AsList();
        var (returns, returnTax, returnCost) = await results.ReadSingleAsync<(decimal, decimal, decimal)>();
        var netSales = totals.Total - totals.Tax - (returns - returnTax);
        return new ReportPage<SalesRegisterRow>
        {
            Items = items,
            TotalCount = (int)totals.Count,
            Limit = query.Limit,
            Offset = query.Offset,
            Totals = new()
            {
                ["subtotal"] = totals.Subtotal,
                ["discount"] = totals.Discount,
                ["tax"] = totals.Tax,
                ["total"] = totals.Total,
                ["paid"] = totals.Paid,
                ["due"] = totals.Due,
                ["cost"] = totals.Cost,
                ["profit"] = totals.Profit,
                ["returns"] = returns,
                ["returnCost"] = returnCost,
                ["netSales"] = netSales,
                ["netProfit"] = netSales - (totals.Cost - returnCost)
            }
        };
    }

    public async Task<ReportPage<SalesByProductRow>> GetByProductAsync(int organizationId, SalesReportQuery query, CancellationToken cancellationToken = default)
    {
        // Net = line total less its tax: after discount and without tax, whether prices include tax or not.
        // Returns in the period come off: their quantity and sales, and the cost of goods back in stock
        // (damaged goods stay a cost).
        const string rows = """
            WITH moved AS (
                SELECT l.product_id, l.base_quantity AS quantity, (l.line_total - l.tax_amount) * i.exchange_rate AS net,
                       l.tax_amount * i.exchange_rate AS tax, l.unit_cost * l.base_quantity AS cost,
                       i.sales_invoice_id AS invoice_id, i.invoice_date AS sale_date
                FROM sales.sales_invoice_line l
                JOIN sales.sales_invoice i ON i.sales_invoice_id = l.sales_invoice_id
                WHERE i.organization_id = @Org AND i.status = 'Posted' AND i.invoice_date BETWEEN @From AND @To
                  AND (CAST(@CustomerId AS int) IS NULL OR i.customer_id = @CustomerId)
                  AND (CAST(@WarehouseId AS int) IS NULL OR i.warehouse_id = @WarehouseId)
                UNION ALL
                SELECT l.product_id, -l.base_quantity, -(l.line_total - l.tax_amount) * r.exchange_rate,
                       -l.tax_amount * r.exchange_rate, CASE WHEN l.is_restockable THEN -l.unit_cost * l.base_quantity ELSE 0 END,
                       NULL, NULL
                FROM sales.sales_return_line l
                JOIN sales.sales_return r ON r.sales_return_id = l.sales_return_id
                WHERE r.organization_id = @Org AND r.status = 'Posted' AND r.return_date BETWEEN @From AND @To
                  AND (CAST(@CustomerId AS int) IS NULL OR r.customer_id = @CustomerId)
                  AND (CAST(@WarehouseId AS int) IS NULL OR r.warehouse_id = @WarehouseId)
            ),
            report AS (
                SELECT p.product_id AS ProductId, p.product_code AS ProductCode, p.product_name AS ProductName,
                       c.category_name AS CategoryName, u.unit_code AS BaseUnitCode,
                       sum(m.quantity) AS Quantity,
                       round(sum(m.net), 4) AS NetAmount,
                       round(sum(m.tax), 4) AS TaxAmount,
                       round(sum(m.cost), 4) AS CostAmount,
                       count(DISTINCT m.invoice_id)::int AS InvoiceCount, max(m.sale_date) AS LastSaleDate
                FROM moved m
                JOIN inventory.product p ON p.product_id = m.product_id
                JOIN inventory.unit u ON u.unit_id = p.base_unit_id
                LEFT JOIN inventory.category c ON c.category_id = p.category_id
                WHERE (CAST(@CategoryIds AS int[]) IS NULL OR p.category_id = ANY(@CategoryIds))
                  AND (CAST(@Pattern AS text) IS NULL OR p.product_code ILIKE @Pattern OR p.product_name ILIKE @Pattern)
                GROUP BY p.product_id, p.product_code, p.product_name, c.category_name, u.unit_code
            ),
            priced AS (
                SELECT *, NetAmount - CostAmount AS Profit,
                       CASE WHEN NetAmount > 0 THEN round((NetAmount - CostAmount) / NetAmount * 100, 2) ELSE 0 END AS MarginPercent,
                       CASE WHEN Quantity > 0 THEN round(NetAmount / Quantity, 6) ELSE 0 END AS AveragePrice
                FROM report
            )
            """;
        var parameters = new
        {
            Org = organizationId,
            query.From,
            query.To,
            query.CustomerId,
            query.WarehouseId,
            CategoryIds = query.CategoryIds?.ToArray(),
            Pattern = RepositoryHelpers.Pattern(query.Search),
            query.Limit,
            query.Offset
        };
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            {rows} SELECT count(*), coalesce(sum(NetAmount), 0), coalesce(sum(TaxAmount), 0), coalesce(sum(CostAmount), 0),
                          coalesce(sum(Profit), 0) FROM priced;
            {rows} SELECT * FROM priced ORDER BY NetAmount DESC, ProductName LIMIT @Limit OFFSET @Offset;
            """, parameters, cancellationToken: cancellationToken));
        var (count, net, tax, cost, profit) = await results.ReadSingleAsync<(long, decimal, decimal, decimal, decimal)>();
        return new ReportPage<SalesByProductRow>
        {
            Items = (await results.ReadAsync<SalesByProductRow>()).AsList(),
            TotalCount = (int)count,
            Limit = query.Limit,
            Offset = query.Offset,
            Totals = new() { ["netAmount"] = net, ["taxAmount"] = tax, ["costAmount"] = cost, ["profit"] = profit }
        };
    }
}
