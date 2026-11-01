using Dapper;
using ThinkTechErpDomain.Models.Accounting;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Accounting;

public interface IAccountingReportRepository
{
    /// <summary>Null when the ledger head isn't one of the organization's sub classes.</summary>
    Task<SubsidiaryLedger?> GetSubsidiaryLedgerAsync(
        int organizationId, int accountId, int? supplierId, int? customerId, DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default);
    /// <summary>Every ledger head with its balance before the period and its movement in it.</summary>
    Task<IReadOnlyList<AccountBalanceRow>> GetBalancesAsync(
        int organizationId, DateOnly from, DateOnly to, bool includeZero, CancellationToken cancellationToken = default);
    /// <summary>Income less expenses posted up to the date.</summary>
    Task<decimal> GetNetProfitAsync(int organizationId, DateOnly asOf, CancellationToken cancellationToken = default);
    Task<decimal> GetPartyBalanceAsync(
        int organizationId, int accountId, int? supplierId, int? customerId, DateOnly asOf,
        CancellationToken cancellationToken = default);
}

public sealed class AccountingReportRepository(IDbConnectionFactory connectionFactory) : IAccountingReportRepository
{
    private const string PartyFilter = """
        AND (CAST(@SupplierId AS int) IS NULL OR e.supplier_id = @SupplierId)
        AND (CAST(@CustomerId AS int) IS NULL OR e.customer_id = @CustomerId)
        """;

    public async Task<SubsidiaryLedger?> GetSubsidiaryLedgerAsync(
        int organizationId, int accountId, int? supplierId, int? customerId, DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        var parameters = new
        {
            Org = organizationId,
            AccountId = accountId,
            SupplierId = supplierId,
            CustomerId = customerId,
            From = from,
            To = to
        };
        var ledger = await connection.QuerySingleOrDefaultAsync<SubsidiaryLedger>(new CommandDefinition($"""
            SELECT a.account_id AS AccountId, a.account_code AS AccountCode, a.account_name AS AccountName,
                   a.nature AS Nature, a.party_type AS PartyType, s.supplier_id AS SupplierId, c.customer_id AS CustomerId,
                   coalesce(s.supplier_code, c.customer_code) AS PartyCode, coalesce(s.supplier_name, c.customer_name) AS PartyName,
                   coalesce((SELECT sum(e.debit_amount - e.credit_amount) FROM accounting.ledger_entry e
                             WHERE e.organization_id = @Org AND e.account_id = a.account_id AND e.voucher_date < @From
                             {PartyFilter}), 0) AS OpeningBalance
            FROM accounting.chart_account a
            LEFT JOIN purchase.supplier s ON s.organization_id = @Org AND s.supplier_id = @SupplierId
            LEFT JOIN sales.customer c ON c.organization_id = @Org AND c.customer_id = @CustomerId
            WHERE a.organization_id = @Org AND a.account_id = @AccountId AND a.account_level = 5
            """, parameters, cancellationToken: cancellationToken));
        if (ledger is null)
            return null;

        var entries = (await connection.QueryAsync<LedgerEntryRow>(new CommandDefinition($"""
            SELECT e.voucher_id AS VoucherId, e.voucher_no AS VoucherNo, e.voucher_type AS VoucherType,
                   e.voucher_date AS VoucherDate, e.reference_no AS ReferenceNo,
                   coalesce(e.line_narration, e.narration) AS Narration,
                   coalesce(s.supplier_name, c.customer_name) AS PartyName,
                   (SELECT string_agg(DISTINCT o.account_name, ', ')
                    FROM accounting.voucher_line ol
                    JOIN accounting.chart_account o ON o.account_id = ol.account_id
                    WHERE ol.voucher_id = e.voucher_id AND ol.voucher_line_id <> e.voucher_line_id
                      AND ((e.debit_amount > 0 AND ol.credit_amount > 0) OR (e.credit_amount > 0 AND ol.debit_amount > 0))) AS Particulars,
                   e.debit_amount AS DebitAmount, e.credit_amount AS CreditAmount
            FROM accounting.ledger_entry e
            LEFT JOIN purchase.supplier s ON s.supplier_id = e.supplier_id
            LEFT JOIN sales.customer c ON c.customer_id = e.customer_id
            WHERE e.organization_id = @Org AND e.account_id = @AccountId AND e.voucher_date BETWEEN @From AND @To
            {PartyFilter}
            ORDER BY e.voucher_date, e.voucher_id, e.line_no
            """, parameters, cancellationToken: cancellationToken))).AsList();

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

    public async Task<IReadOnlyList<AccountBalanceRow>> GetBalancesAsync(
        int organizationId, DateOnly from, DateOnly to, bool includeZero, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return (await connection.QueryAsync<AccountBalanceRow>(new CommandDefinition("""
            SELECT s.account_id AS AccountId, s.account_code AS Code, s.account_name AS Name, s.nature AS Nature,
                   p.account_id AS ParentClassId, p.account_code AS ParentClassCode, p.account_name AS ParentClassName,
                   ac.account_id AS AccountClassId, ac.account_code AS AccountClassCode, ac.account_name AS AccountClassName,
                   mc.account_id AS MainClassId, mc.account_code AS MainClassCode, mc.account_name AS MainClassName,
                   mp.account_id AS MapClassId, mp.account_code AS MapClassCode, mp.account_name AS MapClassName,
                   coalesce(b.opening, 0) AS OpeningBalance, coalesce(b.debit, 0) AS PeriodDebit,
                   coalesce(b.credit, 0) AS PeriodCredit,
                   coalesce(b.opening, 0) + coalesce(b.debit, 0) - coalesce(b.credit, 0) AS ClosingBalance
            FROM accounting.chart_account s
            JOIN accounting.chart_account mp ON mp.account_id = s.parent_account_id
            JOIN accounting.chart_account mc ON mc.account_id = mp.parent_account_id
            JOIN accounting.chart_account ac ON ac.account_id = mc.parent_account_id
            JOIN accounting.chart_account p ON p.account_id = ac.parent_account_id
            LEFT JOIN (
                SELECT account_id,
                       sum(debit_amount - credit_amount) FILTER (WHERE voucher_date < @From) AS opening,
                       sum(debit_amount) FILTER (WHERE voucher_date >= @From) AS debit,
                       sum(credit_amount) FILTER (WHERE voucher_date >= @From) AS credit
                FROM accounting.ledger_entry
                WHERE organization_id = @Org AND voucher_date <= @To
                GROUP BY account_id) b ON b.account_id = s.account_id
            WHERE s.organization_id = @Org AND s.account_level = 5
              AND (@IncludeZero OR coalesce(b.opening, 0) <> 0 OR coalesce(b.debit, 0) <> 0 OR coalesce(b.credit, 0) <> 0)
            ORDER BY p.sort_order, p.account_code, ac.sort_order, ac.account_code, mc.sort_order, mc.account_code,
                     mp.sort_order, mp.account_code, s.sort_order, s.account_code
            """, new { Org = organizationId, From = from, To = to, IncludeZero = includeZero },
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<decimal> GetNetProfitAsync(int organizationId, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleAsync<decimal>(new CommandDefinition("""
            SELECT coalesce(sum(e.credit_amount - e.debit_amount), 0)
            FROM accounting.ledger_entry e
            JOIN accounting.chart_account a ON a.account_id = e.account_id
            WHERE e.organization_id = @Org AND e.voucher_date <= @AsOf AND a.nature IN ('Income', 'Expense')
            """, new { Org = organizationId, AsOf = asOf }, cancellationToken: cancellationToken));
    }

    public async Task<decimal> GetPartyBalanceAsync(
        int organizationId, int accountId, int? supplierId, int? customerId, DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleAsync<decimal>(new CommandDefinition($"""
            SELECT coalesce(sum(e.debit_amount - e.credit_amount), 0)
            FROM accounting.ledger_entry e
            WHERE e.organization_id = @Org AND e.account_id = @AccountId AND e.voucher_date <= @AsOf
            {PartyFilter}
            """, new { Org = organizationId, AccountId = accountId, SupplierId = supplierId, CustomerId = customerId, AsOf = asOf },
            cancellationToken: cancellationToken));
    }
}
