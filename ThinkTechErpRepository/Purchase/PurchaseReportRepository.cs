using Dapper;
using ThinkTechErpDomain.Models.Inventory;
using ThinkTechErpDomain.Models.Purchase;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Purchase;

public sealed class PurchaseReportQuery
{
    public string? Search { get; set; }
    public int? SupplierId { get; set; }
    public string? PurchaseType { get; set; }
    public IReadOnlyCollection<int>? CategoryIds { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    /// <summary>Supplier dues: leave out suppliers with nothing owed either way.</summary>
    public bool OnlyWithBalance { get; set; } = true;
    public int Limit { get; set; } = 50;
    public int Offset { get; set; }
}

public interface IPurchaseReportRepository
{
    Task<SupplierLedger?> GetSupplierLedgerAsync(int organizationId, int supplierId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<ReportPage<SupplierDueRow>> GetSupplierDuesAsync(int organizationId, PurchaseReportQuery query, DateOnly asOf, CancellationToken cancellationToken = default);
    Task<ReportPage<PurchaseRegisterRow>> GetRegisterAsync(int organizationId, PurchaseReportQuery query, CancellationToken cancellationToken = default);
    Task<ReportPage<PurchaseByProductRow>> GetByProductAsync(int organizationId, PurchaseReportQuery query, CancellationToken cancellationToken = default);
}

public sealed class PurchaseReportRepository(IDbConnectionFactory connectionFactory) : IPurchaseReportRepository
{
    public async Task<SupplierLedger?> GetSupplierLedgerAsync(
        int organizationId, int supplierId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var ledger = await connection.QuerySingleOrDefaultAsync<SupplierLedger>(new CommandDefinition("""
            SELECT s.supplier_id AS SupplierId, s.supplier_code AS SupplierCode, s.supplier_name AS SupplierName,
                   coalesce((SELECT sum(l.credit_amount - l.debit_amount) FROM purchase.supplier_ledger l
                             WHERE l.supplier_id = s.supplier_id AND (l.entry_date IS NULL OR l.entry_date < @From)), 0) AS OpeningBalance
            FROM purchase.supplier s
            WHERE s.organization_id = @Org AND s.supplier_id = @SupplierId
            """, new { Org = organizationId, SupplierId = supplierId, From = from }, cancellationToken: cancellationToken));
        if (ledger is null)
            return null;

        var entries = (await connection.QueryAsync<SupplierLedgerEntry>(new CommandDefinition("""
            SELECT l.entry_date AS EntryDate, l.entry_type AS EntryType, l.document_id AS DocumentId,
                   l.document_no AS DocumentNo, l.debit_amount AS DebitAmount, l.credit_amount AS CreditAmount
            FROM purchase.supplier_ledger l
            WHERE l.supplier_id = @SupplierId AND l.entry_date BETWEEN @From AND @To
            ORDER BY l.entry_date, l.document_id, l.entry_type
            """, new { SupplierId = supplierId, From = from, To = to }, cancellationToken: cancellationToken))).AsList();

        var balance = ledger.OpeningBalance;
        foreach (var entry in entries)
        {
            balance += entry.CreditAmount - entry.DebitAmount;
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

    public async Task<ReportPage<SupplierDueRow>> GetSupplierDuesAsync(
        int organizationId, PurchaseReportQuery query, DateOnly asOf, CancellationToken cancellationToken = default)
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
                SELECT i.supplier_id, (i.total_amount - i.settled_amount) * i.exchange_rate AS due,
                       coalesce(i.due_date, i.invoice_date) AS due_date,
                       @AsOf - coalesce(i.due_date, i.invoice_date) AS overdue_days
                FROM purchase.purchase_invoice i
                WHERE i.organization_id = @Org AND i.status = 'Posted' AND i.payment_status <> 'Paid' AND i.supplier_id IS NOT NULL
            ),
            report AS (
                SELECT s.supplier_id AS SupplierId, s.supplier_code AS SupplierCode, s.supplier_name AS SupplierName,
                       s.mobile AS Mobile,
                       coalesce((SELECT sum(l.credit_amount - l.debit_amount) FROM purchase.supplier_ledger l
                                 WHERE l.supplier_id = s.supplier_id), 0) AS Balance,
                       (SELECT count(*) FROM open_invoice o WHERE o.supplier_id = s.supplier_id)::int AS OpenInvoiceCount,
                       coalesce((SELECT round(sum(o.due), 4) FROM open_invoice o WHERE o.supplier_id = s.supplier_id AND o.overdue_days <= 0), 0) AS NotDue,
                       coalesce((SELECT round(sum(o.due), 4) FROM open_invoice o WHERE o.supplier_id = s.supplier_id AND o.overdue_days BETWEEN 1 AND 30), 0) AS Days1To30,
                       coalesce((SELECT round(sum(o.due), 4) FROM open_invoice o WHERE o.supplier_id = s.supplier_id AND o.overdue_days BETWEEN 31 AND 60), 0) AS Days31To60,
                       coalesce((SELECT round(sum(o.due), 4) FROM open_invoice o WHERE o.supplier_id = s.supplier_id AND o.overdue_days BETWEEN 61 AND 90), 0) AS Days61To90,
                       coalesce((SELECT round(sum(o.due), 4) FROM open_invoice o WHERE o.supplier_id = s.supplier_id AND o.overdue_days > 90), 0) AS Over90,
                       (SELECT min(o.due_date) FROM open_invoice o WHERE o.supplier_id = s.supplier_id) AS OldestDueDate
                FROM purchase.supplier s
                WHERE s.organization_id = @Org AND s.is_supplier
                  AND (CAST(@Pattern AS text) IS NULL OR s.supplier_code ILIKE @Pattern OR s.supplier_name ILIKE @Pattern
                       OR s.mobile ILIKE @Pattern)
            ),
            filtered AS (SELECT * FROM report WHERE NOT @OnlyWithBalance OR Balance <> 0 OR OpenInvoiceCount > 0)
            """;
        await using var connection = connectionFactory.CreateConnection();
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            {rows} SELECT count(*), coalesce(sum(Balance), 0), coalesce(sum(Days1To30 + Days31To60 + Days61To90 + Over90), 0) FROM filtered;
            {rows} SELECT * FROM filtered ORDER BY Balance DESC, SupplierName LIMIT @Limit OFFSET @Offset;
            """, parameters, cancellationToken: cancellationToken));
        var (count, balance, overdue) = await results.ReadSingleAsync<(long, decimal, decimal)>();
        return new ReportPage<SupplierDueRow>
        {
            Items = (await results.ReadAsync<SupplierDueRow>()).AsList(),
            TotalCount = (int)count,
            Limit = query.Limit,
            Offset = query.Offset,
            Totals = new() { ["balance"] = balance, ["overdue"] = overdue }
        };
    }

    public async Task<ReportPage<PurchaseRegisterRow>> GetRegisterAsync(int organizationId, PurchaseReportQuery query, CancellationToken cancellationToken = default)
    {
        const string filter = """
            FROM purchase.purchase_invoice i
            LEFT JOIN purchase.supplier s ON s.supplier_id = i.supplier_id
            JOIN public.lookup_data cur ON cur.lookup_data_id = i.currency_lookup_data_id
            WHERE i.organization_id = @Org AND i.status = 'Posted' AND i.invoice_date BETWEEN @From AND @To
              AND (CAST(@SupplierId AS int) IS NULL OR i.supplier_id = @SupplierId)
              AND (CAST(@PurchaseType AS text) IS NULL OR i.purchase_type = @PurchaseType)
              AND (CAST(@Pattern AS text) IS NULL OR i.invoice_no ILIKE @Pattern OR i.supplier_invoice_no ILIKE @Pattern
                   OR s.supplier_name ILIKE @Pattern OR i.supplier_name ILIKE @Pattern)
            """;
        var parameters = new
        {
            Org = organizationId,
            query.From,
            query.To,
            query.SupplierId,
            query.PurchaseType,
            Pattern = RepositoryHelpers.Pattern(query.Search),
            query.Limit,
            query.Offset
        };
        await using var connection = connectionFactory.CreateConnection();
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT count(*),
                   coalesce(round(sum(i.subtotal_amount * i.exchange_rate), 4), 0),
                   coalesce(round(sum(i.discount_amount * i.exchange_rate), 4), 0),
                   coalesce(round(sum(i.tax_amount * i.exchange_rate), 4), 0),
                   coalesce(round(sum(i.total_amount * i.exchange_rate), 4), 0),
                   coalesce(round(sum((i.total_amount - i.settled_amount) * i.exchange_rate), 4), 0) {filter};
            SELECT i.purchase_invoice_id AS InvoiceId, i.invoice_no AS InvoiceNo, i.invoice_date AS InvoiceDate,
                   i.purchase_type AS PurchaseType, coalesce(s.supplier_name, i.supplier_name) AS SupplierName,
                   i.supplier_invoice_no AS SupplierInvoiceNo, i.tax_invoice_no AS TaxInvoiceNo, cur.lookup_data_name AS Currency,
                   round(i.subtotal_amount * i.exchange_rate, 4) AS SubtotalAmount,
                   round(i.discount_amount * i.exchange_rate, 4) AS DiscountAmount,
                   round(i.tax_amount * i.exchange_rate, 4) AS TaxAmount,
                   round(i.charge_amount * i.exchange_rate, 4) AS ChargeAmount,
                   round(i.total_amount * i.exchange_rate, 4) AS TotalAmount,
                   round(i.settled_amount * i.exchange_rate, 4) AS SettledAmount,
                   round((i.total_amount - i.settled_amount) * i.exchange_rate, 4) AS DueAmount,
                   i.payment_status AS PaymentStatus
            {filter}
            ORDER BY i.invoice_date, i.purchase_invoice_id
            LIMIT @Limit OFFSET @Offset;
            """, parameters, cancellationToken: cancellationToken));
        var (count, subtotal, discount, tax, total, due) = await results.ReadSingleAsync<(long, decimal, decimal, decimal, decimal, decimal)>();
        return new ReportPage<PurchaseRegisterRow>
        {
            Items = (await results.ReadAsync<PurchaseRegisterRow>()).AsList(),
            TotalCount = (int)count,
            Limit = query.Limit,
            Offset = query.Offset,
            Totals = new() { ["subtotal"] = subtotal, ["discount"] = discount, ["tax"] = tax, ["total"] = total, ["due"] = due }
        };
    }

    public async Task<ReportPage<PurchaseByProductRow>> GetByProductAsync(int organizationId, PurchaseReportQuery query, CancellationToken cancellationToken = default)
    {
        const string rows = """
            WITH report AS (
                SELECT p.product_id AS ProductId, p.product_code AS ProductCode, p.product_name AS ProductName,
                       c.category_name AS CategoryName, u.unit_code AS BaseUnitCode,
                       sum(l.base_quantity) AS Quantity,
                       round(sum((l.quantity * l.unit_price - l.discount_amount) * i.exchange_rate), 4) AS NetAmount,
                       round(sum(l.tax_amount * i.exchange_rate), 4) AS TaxAmount,
                       CASE WHEN sum(l.base_quantity) > 0
                            THEN round(sum((l.quantity * l.unit_price - l.discount_amount) * i.exchange_rate) / sum(l.base_quantity), 6)
                            ELSE 0 END AS AveragePrice,
                       count(DISTINCT i.purchase_invoice_id)::int AS InvoiceCount, max(i.invoice_date) AS LastPurchaseDate
                FROM purchase.purchase_invoice_line l
                JOIN purchase.purchase_invoice i ON i.purchase_invoice_id = l.purchase_invoice_id
                JOIN inventory.product p ON p.product_id = l.product_id
                JOIN inventory.unit u ON u.unit_id = p.base_unit_id
                LEFT JOIN inventory.category c ON c.category_id = p.category_id
                WHERE i.organization_id = @Org AND i.status = 'Posted' AND i.invoice_date BETWEEN @From AND @To
                  AND (CAST(@SupplierId AS int) IS NULL OR i.supplier_id = @SupplierId)
                  AND (CAST(@CategoryIds AS int[]) IS NULL OR p.category_id = ANY(@CategoryIds))
                  AND (CAST(@Pattern AS text) IS NULL OR p.product_code ILIKE @Pattern OR p.product_name ILIKE @Pattern)
                GROUP BY p.product_id, p.product_code, p.product_name, c.category_name, u.unit_code
            )
            """;
        var parameters = new
        {
            Org = organizationId,
            query.From,
            query.To,
            query.SupplierId,
            CategoryIds = query.CategoryIds?.ToArray(),
            Pattern = RepositoryHelpers.Pattern(query.Search),
            query.Limit,
            query.Offset
        };
        await using var connection = connectionFactory.CreateConnection();
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            {rows} SELECT count(*), coalesce(sum(NetAmount), 0), coalesce(sum(TaxAmount), 0) FROM report;
            {rows} SELECT * FROM report ORDER BY NetAmount DESC, ProductName LIMIT @Limit OFFSET @Offset;
            """, parameters, cancellationToken: cancellationToken));
        var (count, net, tax) = await results.ReadSingleAsync<(long, decimal, decimal)>();
        return new ReportPage<PurchaseByProductRow>
        {
            Items = (await results.ReadAsync<PurchaseByProductRow>()).AsList(),
            TotalCount = (int)count,
            Limit = query.Limit,
            Offset = query.Offset,
            Totals = new() { ["netAmount"] = net, ["taxAmount"] = tax }
        };
    }
}
