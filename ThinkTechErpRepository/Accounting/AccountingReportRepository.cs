using Dapper;
using ThinkTechErpDomain.Models.Accounting;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Accounting;

public interface IAccountingReportRepository
{
    /// <summary>Null when the ledger head isn't a sub class of the chart.</summary>
    Task<SubsidiaryLedger?> GetSubsidiaryLedgerAsync(
        int organizationId, int accountId, int? partyId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    /// <summary>Every ledger head with its balance before the period and its movement in it.</summary>
    Task<IReadOnlyList<AccountBalanceRow>> GetBalancesAsync(
        int organizationId, DateOnly from, DateOnly to, bool includeZero, CancellationToken cancellationToken = default);
    /// <summary>Income less expenses posted up to the date.</summary>
    Task<decimal> GetNetProfitAsync(int organizationId, DateOnly asOf, CancellationToken cancellationToken = default);
    Task<decimal> GetPartyBalanceAsync(
        int organizationId, int accountId, int partyId, DateOnly asOf, CancellationToken cancellationToken = default);
}

/// <summary>Ledger heads are the acc_* sub classes, placed in the chart by the accounting.ledger_head view.</summary>
public sealed class AccountingReportRepository(IDbConnectionFactory connectionFactory) : IAccountingReportRepository
{
    private const string PartyFilter = "AND (CAST(@PartyId AS int) IS NULL OR e.supplier_id = @PartyId)";

    public async Task<SubsidiaryLedger?> GetSubsidiaryLedgerAsync(
        int organizationId, int accountId, int? partyId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        var parameters = new { Org = organizationId, AccountId = accountId, PartyId = partyId, From = from, To = to };
        var ledger = await connection.QuerySingleOrDefaultAsync<SubsidiaryLedger>(new CommandDefinition($"""
            SELECT h.acc_sub_class_id AS AccountId, coalesce(h.sub_code, '') AS AccountCode,
                   coalesce(h.sub_name, '') AS AccountName, coalesce(h.nature, '') AS Nature, p.supplier_id AS PartyId,
                   p.supplier_code AS PartyCode, p.supplier_name AS PartyName,
                   coalesce((SELECT sum(e.debit_amount - e.credit_amount) FROM accounting.ledger_entry e
                             WHERE e.organization_id = @Org AND e.acc_sub_class_id = h.acc_sub_class_id AND e.voucher_date < @From
                             {PartyFilter}), 0) AS OpeningBalance
            FROM accounting.ledger_head h
            LEFT JOIN purchase.supplier p ON p.organization_id = @Org AND p.supplier_id = @PartyId
            WHERE h.acc_sub_class_id = @AccountId
            """, parameters, cancellationToken: cancellationToken));
        if (ledger is null)
            return null;

        var entries = (await connection.QueryAsync<LedgerEntryRow>(new CommandDefinition($"""
            SELECT e.voucher_id AS VoucherId, e.voucher_no AS VoucherNo, e.voucher_type AS VoucherType,
                   e.voucher_date AS VoucherDate, e.reference_no AS ReferenceNo,
                   coalesce(e.line_narration, e.narration) AS Narration, p.supplier_name AS PartyName,
                   (SELECT string_agg(DISTINCT o.sub_name, ', ')
                    FROM accounting.voucher_line ol
                    JOIN accounting.acc_sub_class o ON o.acc_sub_class_id = ol.acc_sub_class_id
                    WHERE ol.voucher_id = e.voucher_id AND ol.voucher_line_id <> e.voucher_line_id
                      AND ((e.debit_amount > 0 AND ol.credit_amount > 0) OR (e.credit_amount > 0 AND ol.debit_amount > 0))) AS Particulars,
                   e.debit_amount AS DebitAmount, e.credit_amount AS CreditAmount
            FROM accounting.ledger_entry e
            LEFT JOIN purchase.supplier p ON p.supplier_id = e.supplier_id
            WHERE e.organization_id = @Org AND e.acc_sub_class_id = @AccountId AND e.voucher_date BETWEEN @From AND @To
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
            SELECT h.acc_sub_class_id AS AccountId, coalesce(h.sub_code, '') AS Code, coalesce(h.sub_name, '') AS Name,
                   coalesce(h.nature, '') AS Nature,
                   h.acc_ac_parent_class_id AS ParentClassId, coalesce(h.parent_code, '') AS ParentClassCode,
                   coalesce(h.parent_name, '') AS ParentClassName,
                   h.acc_ac_class_id AS AccountClassId, coalesce(h.ac_code, '') AS AccountClassCode,
                   coalesce(h.class_name, '') AS AccountClassName,
                   h.acc_main_class_id AS MainClassId, coalesce(h.main_code, '') AS MainClassCode,
                   coalesce(h.main_name, '') AS MainClassName,
                   h.acc_map_class_id AS MapClassId, coalesce(h.map_code, '') AS MapClassCode,
                   coalesce(h.map_name, '') AS MapClassName,
                   coalesce(b.opening, 0) AS OpeningBalance, coalesce(b.debit, 0) AS PeriodDebit,
                   coalesce(b.credit, 0) AS PeriodCredit,
                   coalesce(b.opening, 0) + coalesce(b.debit, 0) - coalesce(b.credit, 0) AS ClosingBalance
            FROM accounting.ledger_head h
            LEFT JOIN (
                SELECT acc_sub_class_id,
                       sum(debit_amount - credit_amount) FILTER (WHERE voucher_date < @From) AS opening,
                       sum(debit_amount) FILTER (WHERE voucher_date >= @From) AS debit,
                       sum(credit_amount) FILTER (WHERE voucher_date >= @From) AS credit
                FROM accounting.ledger_entry
                WHERE organization_id = @Org AND voucher_date <= @To
                GROUP BY acc_sub_class_id) b ON b.acc_sub_class_id = h.acc_sub_class_id
            WHERE @IncludeZero OR coalesce(b.opening, 0) <> 0 OR coalesce(b.debit, 0) <> 0 OR coalesce(b.credit, 0) <> 0
            ORDER BY h.parent_code, h.ac_code, h.mc_display_order NULLS LAST, h.main_code, h.map_code, h.sub_code
            """, new { Org = organizationId, From = from, To = to, IncludeZero = includeZero },
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<decimal> GetNetProfitAsync(int organizationId, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleAsync<decimal>(new CommandDefinition("""
            SELECT coalesce(sum(e.credit_amount - e.debit_amount), 0)
            FROM accounting.ledger_entry e
            JOIN accounting.ledger_head h ON h.acc_sub_class_id = e.acc_sub_class_id
            WHERE e.organization_id = @Org AND e.voucher_date <= @AsOf AND h.nature IN ('Income', 'Expense')
            """, new { Org = organizationId, AsOf = asOf }, cancellationToken: cancellationToken));
    }

    public async Task<decimal> GetPartyBalanceAsync(
        int organizationId, int accountId, int partyId, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleAsync<decimal>(new CommandDefinition($"""
            SELECT coalesce(sum(e.debit_amount - e.credit_amount), 0)
            FROM accounting.ledger_entry e
            WHERE e.organization_id = @Org AND e.acc_sub_class_id = @AccountId AND e.voucher_date <= @AsOf
            {PartyFilter}
            """, new { Org = organizationId, AccountId = accountId, PartyId = partyId, AsOf = asOf },
            cancellationToken: cancellationToken));
    }
}
