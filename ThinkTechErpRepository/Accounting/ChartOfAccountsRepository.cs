using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Accounting;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Accounting;

/// <summary>An account to add with the standard chart; ParentCode is empty for a parent class.</summary>
public sealed record StandardChartAccount(
    int Level, string Code, string? ParentCode, string Name, string Nature, string? LedgerType = null, string? PartyType = null);

public interface IChartOfAccountsRepository
{
    /// <summary>The whole chart, every level, as a flat list (ParentId links it into a tree).</summary>
    Task<IReadOnlyList<ChartAccount>> GetAccountsAsync(int organizationId, CancellationToken cancellationToken = default);
    Task<ChartAccount?> GetAccountAsync(int organizationId, int accountId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LedgerHeadOption>> GetLedgerHeadsAsync(
        int organizationId, string? search, IReadOnlyCollection<string>? ledgerTypes, string? partyType,
        IReadOnlyCollection<int>? ids, bool includeInactive, int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PartyOption>> GetPartiesAsync(
        int organizationId, string partyType, string? search, IReadOnlyCollection<int>? ids, int limit,
        CancellationToken cancellationToken = default);
    /// <summary>Adds an account at account.Level under account.ParentId; an empty Code takes the next one.</summary>
    Task<int> CreateAccountAsync(int organizationId, ChartAccount account, int userId, CancellationToken cancellationToken = default);
    /// <summary>Saves an account; a new nature (or parent) is copied to every account below it.</summary>
    Task<bool> UpdateAccountAsync(int organizationId, ChartAccount account, int userId, CancellationToken cancellationToken = default);
    /// <summary>Ticking activates the account, its branch and its parents; clearing deactivates the branch.</summary>
    Task<bool> SetActiveAsync(int organizationId, int accountId, bool isActive, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteAccountAsync(int organizationId, int accountId, CancellationToken cancellationToken = default);
    Task<bool> CodeExistsAsync(int organizationId, string code, int? excludeId, CancellationToken cancellationToken = default);
    /// <summary>Adds the accounts in order (parents first); refused when the organization has a chart already.</summary>
    Task<int> ImportAsync(int organizationId, IReadOnlyList<StandardChartAccount> accounts, int userId, CancellationToken cancellationToken = default);
}

public sealed class ChartOfAccountsRepository(IDbConnectionFactory connectionFactory) : IChartOfAccountsRepository
{
    private const string AccountSelect = """
        SELECT a.account_id AS Id, a.parent_account_id AS ParentId, a.account_level AS Level, a.account_code AS Code,
               a.account_name AS Name, a.nature AS Nature, a.ledger_type AS LedgerType, a.party_type AS PartyType,
               a.sort_order AS SortOrder, a.description AS Description, a.is_active AS IsActive,
               (SELECT count(*) FROM accounting.chart_account c WHERE c.parent_account_id = a.account_id)::int AS ChildCount,
               EXISTS (SELECT 1 FROM accounting.voucher_line l WHERE l.account_id = a.account_id) AS HasEntries,
               a.created_at AT TIME ZONE 'utc' AS CreatedAt, a.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM accounting.chart_account a
        """;

    // The account and everything below it.
    private const string Branch = """
        WITH RECURSIVE branch AS (
            SELECT account_id FROM accounting.chart_account WHERE organization_id = @Org AND account_id = @Id
            UNION ALL
            SELECT c.account_id FROM accounting.chart_account c JOIN branch b ON c.parent_account_id = b.account_id)
        """;

    public async Task<IReadOnlyList<ChartAccount>> GetAccountsAsync(int organizationId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return (await connection.QueryAsync<ChartAccount>(new CommandDefinition(
            $"{AccountSelect} WHERE a.organization_id = @Org ORDER BY a.account_level, a.sort_order, a.account_code",
            new { Org = organizationId }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<ChartAccount?> GetAccountAsync(int organizationId, int accountId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleOrDefaultAsync<ChartAccount>(new CommandDefinition(
            $"{AccountSelect} WHERE a.organization_id = @Org AND a.account_id = @Id",
            new { Org = organizationId, Id = accountId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<LedgerHeadOption>> GetLedgerHeadsAsync(
        int organizationId, string? search, IReadOnlyCollection<string>? ledgerTypes, string? partyType,
        IReadOnlyCollection<int>? ids, bool includeInactive, int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return (await connection.QueryAsync<LedgerHeadOption>(new CommandDefinition("""
            SELECT s.account_id AS Id, s.account_code AS Code, s.account_name AS Name, s.nature AS Nature,
                   s.ledger_type AS LedgerType, s.party_type AS PartyType,
                   concat_ws(' › ', main.account_name, map.account_name) AS Path, s.is_active AS IsActive
            FROM accounting.chart_account s
            JOIN accounting.chart_account map ON map.account_id = s.parent_account_id
            JOIN accounting.chart_account main ON main.account_id = map.parent_account_id
            WHERE s.organization_id = @Org AND s.account_level = 5
              AND (CAST(@Ids AS int[]) IS NULL OR s.account_id = ANY(@Ids))
              AND (CAST(@Ids AS int[]) IS NOT NULL OR @IncludeInactive OR s.is_active)
              AND (CAST(@Pattern AS text) IS NULL OR s.account_code ILIKE @Pattern OR s.account_name ILIKE @Pattern
                   OR map.account_name ILIKE @Pattern)
              AND (CAST(@LedgerTypes AS text[]) IS NULL OR s.ledger_type = ANY(@LedgerTypes))
              AND (CAST(@PartyType AS text) IS NULL OR s.party_type = @PartyType)
            ORDER BY s.is_active DESC, (s.account_code ILIKE @Prefix) DESC, s.account_code
            LIMIT @Limit
            """, new
            {
                Org = organizationId,
                Ids = ids?.ToArray(),
                IncludeInactive = includeInactive,
                Pattern = RepositoryHelpers.Pattern(search),
                Prefix = search is null ? null : search.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%",
                LedgerTypes = ledgerTypes?.ToArray(),
                PartyType = partyType,
                Limit = limit
            }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<PartyOption>> GetPartiesAsync(
        int organizationId, string partyType, string? search, IReadOnlyCollection<int>? ids, int limit,
        CancellationToken cancellationToken = default)
    {
        var (table, key, code, name) = partyType == PartyType.Supplier
            ? ("purchase.supplier", "supplier_id", "supplier_code", "supplier_name")
            : ("sales.customer", "customer_id", "customer_code", "customer_name");
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return (await connection.QueryAsync<PartyOption>(new CommandDefinition($"""
            SELECT p.{key} AS Id, p.{code} AS Code, p.{name} AS Name, p.mobile AS Mobile, p.is_active AS IsActive
            FROM {table} p
            WHERE p.organization_id = @Org
              AND (CAST(@Ids AS int[]) IS NULL OR p.{key} = ANY(@Ids))
              AND (CAST(@Pattern AS text) IS NULL OR p.{code} ILIKE @Pattern OR p.{name} ILIKE @Pattern OR p.mobile ILIKE @Pattern)
            ORDER BY p.is_active DESC, p.{name}
            LIMIT @Limit
            """, new { Org = organizationId, Ids = ids?.ToArray(), Pattern = RepositoryHelpers.Pattern(search), Limit = limit },
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<int> CreateAccountAsync(int organizationId, ChartAccount account, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(account.Code))
            account.Code = await NextCodeAsync(connection, transaction, organizationId, account, cancellationToken);

        var id = await InsertAsync(connection, transaction, organizationId, account, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    /// <summary>The parent's code plus the next free number among its children: 1101 → 110101, 110102, ...</summary>
    private static async Task<string> NextCodeAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, ChartAccount account,
        CancellationToken cancellationToken)
    {
        // Locking the parent (or, for parent classes, the organization's chart) serializes concurrent adds.
        var parentCode = account.ParentId is null
            ? await connection.QuerySingleAsync<string>(new CommandDefinition(
                "SELECT '' FROM security.organization WHERE organization_id = @Org FOR UPDATE",
                new { Org = organizationId }, transaction, cancellationToken: cancellationToken))
            : await connection.QuerySingleAsync<string>(new CommandDefinition(
                "SELECT account_code FROM accounting.chart_account WHERE organization_id = @Org AND account_id = @Id FOR UPDATE",
                new { Org = organizationId, Id = account.ParentId }, transaction, cancellationToken: cancellationToken));

        var last = await connection.QuerySingleAsync<long>(new CommandDefinition("""
            SELECT coalesce(max(CAST(substr(account_code, length(@ParentCode) + 1) AS bigint)), 0)
            FROM accounting.chart_account
            WHERE organization_id = @Org AND parent_account_id IS NOT DISTINCT FROM @ParentId
              AND left(account_code, length(@ParentCode)) = @ParentCode
              AND substr(account_code, length(@ParentCode) + 1) ~ '^[0-9]{1,15}$'
            """, new { Org = organizationId, account.ParentId, ParentCode = parentCode }, transaction,
            cancellationToken: cancellationToken));

        var digits = AccountingRules.CodeDigits(account.Level);
        for (var next = last + 1; next < last + 100; next++)
        {
            var code = parentCode + next.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(digits, '0');
            // Skip codes typed by hand elsewhere in the chart.
            if (!await connection.QuerySingleAsync<bool>(new CommandDefinition("""
                    SELECT EXISTS (SELECT 1 FROM accounting.chart_account WHERE organization_id = @Org AND lower(account_code) = lower(@Code))
                    """, new { Org = organizationId, Code = code }, transaction, cancellationToken: cancellationToken)))
                return code;
        }
        throw new BusinessConflictException("Couldn't find a free code under this account. Enter the code.");
    }

    private static Task<int> InsertAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, ChartAccount account, int userId,
        CancellationToken cancellationToken)
        => connection.QuerySingleAsync<int>(new CommandDefinition($"""
            INSERT INTO accounting.chart_account (
                organization_id, parent_account_id, account_level, account_code, account_name, nature, ledger_type,
                party_type, sort_order, description, is_active, created_at, created_by)
            VALUES (
                @Org, @ParentId, @Level, @Code, @Name, @Nature, @LedgerType,
                @PartyType, @SortOrder, @Description, @IsActive, {SecurityDb.NowUtc}, @UserId)
            RETURNING account_id
            """, new
            {
                Org = organizationId,
                account.ParentId,
                Level = (short)account.Level,
                account.Code,
                account.Name,
                account.Nature,
                account.LedgerType,
                account.PartyType,
                account.SortOrder,
                account.Description,
                account.IsActive,
                UserId = userId
            }, transaction, cancellationToken: cancellationToken));

    public async Task<bool> UpdateAccountAsync(int organizationId, ChartAccount account, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var parameters = new
        {
            Org = organizationId,
            account.Id,
            account.ParentId,
            account.Code,
            account.Name,
            account.Nature,
            account.LedgerType,
            account.PartyType,
            account.SortOrder,
            account.Description,
            account.IsActive,
            UserId = userId
        };
        var updated = await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE accounting.chart_account
            SET parent_account_id = @ParentId, account_code = @Code, account_name = @Name, ledger_type = @LedgerType,
                party_type = @PartyType, sort_order = @SortOrder, description = @Description, is_active = @IsActive,
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE organization_id = @Org AND account_id = @Id
            """, parameters, transaction, cancellationToken: cancellationToken)) > 0;
        if (updated)
            await connection.ExecuteAsync(new CommandDefinition($"""
                {Branch}
                UPDATE accounting.chart_account SET nature = @Nature
                WHERE account_id IN (SELECT account_id FROM branch) AND nature <> @Nature
                """, parameters, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return updated;
    }

    public async Task<bool> SetActiveAsync(int organizationId, int accountId, bool isActive, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        var parameters = new { Org = organizationId, Id = accountId, IsActive = isActive, UserId = userId };
        var sql = isActive
            ? $"""
              {Branch},
              parents AS (
                  SELECT parent_account_id AS account_id FROM accounting.chart_account WHERE account_id = @Id
                  UNION ALL
                  SELECT c.parent_account_id FROM accounting.chart_account c JOIN parents p ON c.account_id = p.account_id
                  WHERE c.parent_account_id IS NOT NULL)
              UPDATE accounting.chart_account SET is_active = TRUE, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
              WHERE organization_id = @Org AND NOT is_active
                AND (account_id IN (SELECT account_id FROM branch) OR account_id IN (SELECT account_id FROM parents))
              """
            : $"""
              {Branch}
              UPDATE accounting.chart_account SET is_active = FALSE, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
              WHERE organization_id = @Org AND is_active AND account_id IN (SELECT account_id FROM branch)
              """;
        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        return await connection.QuerySingleAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM accounting.chart_account WHERE organization_id = @Org AND account_id = @Id)",
            parameters, cancellationToken: cancellationToken));
    }

    public async Task<DeleteOutcome> DeleteAccountAsync(int organizationId, int accountId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        // Accounts below it and voucher lines on it both hold a foreign key, so either answers InUse.
        return await RepositoryHelpers.DeleteAsync(connection, new CommandDefinition(
            "DELETE FROM accounting.chart_account WHERE organization_id = @Org AND account_id = @Id",
            new { Org = organizationId, Id = accountId }, cancellationToken: cancellationToken));
    }

    public async Task<bool> CodeExistsAsync(int organizationId, string code, int? excludeId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (SELECT 1 FROM accounting.chart_account WHERE organization_id = @Org AND lower(account_code) = lower(@Code)
                           AND (CAST(@ExcludeId AS int) IS NULL OR account_id <> @ExcludeId))
            """, new { Org = organizationId, Code = code, ExcludeId = excludeId }, cancellationToken: cancellationToken));
    }

    public async Task<int> ImportAsync(int organizationId, IReadOnlyList<StandardChartAccount> accounts, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT 1 FROM security.organization WHERE organization_id = @Org FOR UPDATE",
            new { Org = organizationId }, transaction, cancellationToken: cancellationToken));
        if (await connection.QuerySingleAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM accounting.chart_account WHERE organization_id = @Org)",
                new { Org = organizationId }, transaction, cancellationToken: cancellationToken)))
            throw new BusinessConflictException("The organization has a chart of accounts already.");

        var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sortOrder = 0;
        foreach (var account in accounts)
        {
            ids[account.Code] = await InsertAsync(connection, transaction, organizationId, new ChartAccount
            {
                ParentId = account.ParentCode is null ? null : ids[account.ParentCode],
                Level = account.Level,
                Code = account.Code,
                Name = account.Name,
                Nature = account.Nature,
                LedgerType = account.Level == AccountingRules.LedgerLevel ? account.LedgerType ?? LedgerType.General : null,
                PartyType = account.PartyType,
                SortOrder = ++sortOrder,
                IsActive = true
            }, userId, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return ids.Count;
    }
}
