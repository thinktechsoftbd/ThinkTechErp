using System.Globalization;
using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Accounting;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Accounting;

/// <summary>
/// The classic chart of accounts in the accounting.acc_* tables, shared by every organization. Codes follow
/// MediDesk: a main class is its account class's code plus two digits (101 → 10101), a map class takes the next
/// four digits under its main class (0001), and a sub class is its main code plus four digits (102040001).
/// </summary>
public interface IAccountClassRepository
{
    /// <summary>The whole chart as a tree: parent classes, each with its account classes, and so on down.</summary>
    Task<IReadOnlyList<AccountClassNode>> GetTreeAsync(CancellationToken cancellationToken = default);
    /// <summary>Adds a main, map or sub class under parentId with the next code; null when the parent no longer exists.</summary>
    Task<AccountClassNode?> CreateAsync(string kind, int parentId, string name, CancellationToken cancellationToken = default);
    Task<AccountClassNode?> RenameAsync(string kind, int id, string name, CancellationToken cancellationToken = default);
    Task<bool> HasChildrenAsync(string kind, int id, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteAsync(string kind, int id, CancellationToken cancellationToken = default);
}

public sealed class AccountClassRepository(IDbConnectionFactory connectionFactory) : IAccountClassRepository
{
    // Table, key, code and name columns of the editable levels; the level below and its key to this one.
    private sealed record Level(string Table, string Key, string Code, string Name, string? ChildTable = null, string? ChildKey = null);

    private static readonly Dictionary<string, Level> Levels = new(StringComparer.Ordinal)
    {
        [AccountClassKind.Main] = new("accounting.acc_main_class", "acc_main_class_id", "main_code", "main_name",
            "accounting.acc_map_class", "acc_main_class_id"),
        [AccountClassKind.Map] = new("accounting.acc_map_class", "acc_map_class_id", "map_code", "map_name",
            "accounting.acc_sub_class", "acc_map_class_id"),
        [AccountClassKind.Sub] = new("accounting.acc_sub_class", "acc_sub_class_id", "sub_code", "sub_name")
    };

    private sealed class Row
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
        public string? Code { get; set; }
        public string? Name { get; set; }
    }

    // The codes a new child copies from the node it goes under.
    private sealed class ParentCodes
    {
        public string? AcCode { get; set; }
        public string? MainCode { get; set; }
        public string? MapCode { get; set; }
        public int MainId { get; set; }
    }

    public async Task<IReadOnlyList<AccountClassNode>> GetTreeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition("""
            SELECT acc_ac_parent_class_id AS Id, 0 AS ParentId, parent_code AS Code, name AS Name
            FROM accounting.acc_ac_parent_class ORDER BY parent_code, acc_ac_parent_class_id;
            SELECT acc_ac_class_id AS Id, acc_ac_parent_class_id AS ParentId, ac_code AS Code, class_name AS Name
            FROM accounting.acc_ac_class ORDER BY ac_code, acc_ac_class_id;
            SELECT acc_main_class_id AS Id, acc_ac_class_id AS ParentId, main_code AS Code, main_name AS Name
            FROM accounting.acc_main_class ORDER BY mc_display_order NULLS LAST, main_code, acc_main_class_id;
            SELECT acc_map_class_id AS Id, acc_main_class_id AS ParentId, map_code AS Code, map_name AS Name
            FROM accounting.acc_map_class ORDER BY map_code, acc_map_class_id;
            SELECT acc_sub_class_id AS Id, acc_map_class_id AS ParentId, sub_code AS Code, sub_name AS Name
            FROM accounting.acc_sub_class ORDER BY sub_code, acc_sub_class_id;
            """, cancellationToken: cancellationToken));

        var parentRows = await results.ReadAsync<Row>();
        var acRows = await results.ReadAsync<Row>();
        var mainRows = await results.ReadAsync<Row>();
        var mapRows = await results.ReadAsync<Row>();
        var subRows = await results.ReadAsync<Row>();

        // Built from the bottom up, so each node gets its children as it is made.
        var subs = Group(subRows, AccountClassKind.Sub, null);
        var maps = Group(mapRows, AccountClassKind.Map, subs);
        var mains = Group(mainRows, AccountClassKind.Main, maps);
        var acClasses = Group(acRows, AccountClassKind.Account, mains);
        return parentRows.Select(row => Node(AccountClassKind.Parent, row, acClasses)).ToList();
    }

    private static Dictionary<int, List<AccountClassNode>> Group(
        IEnumerable<Row> rows, string kind, Dictionary<int, List<AccountClassNode>>? children)
    {
        var groups = new Dictionary<int, List<AccountClassNode>>();
        foreach (var row in rows)
        {
            if (!groups.TryGetValue(row.ParentId, out var list))
                groups[row.ParentId] = list = [];
            list.Add(Node(kind, row, children));
        }
        return groups;
    }

    // char(n) columns (parent_code, map_code) come back space padded.
    private static AccountClassNode Node(string kind, Row row, Dictionary<int, List<AccountClassNode>>? children) => new()
    {
        Kind = kind,
        Id = row.Id,
        Code = row.Code?.Trim(),
        Name = row.Name?.Trim(),
        Children = children is not null && children.TryGetValue(row.Id, out var list) ? list : []
    };

    public async Task<AccountClassNode?> CreateAsync(string kind, int parentId, string name, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var node = kind switch
        {
            AccountClassKind.Main => await CreateMainAsync(connection, transaction, parentId, name, cancellationToken),
            AccountClassKind.Map => await CreateMapAsync(connection, transaction, parentId, name, cancellationToken),
            _ => await CreateSubAsync(connection, transaction, parentId, name, cancellationToken)
        };
        if (node is not null)
            await transaction.CommitAsync(cancellationToken);
        return node;
    }

    private static async Task<AccountClassNode?> CreateMainAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int acClassId, string name, CancellationToken cancellationToken)
    {
        // Locking the parent serializes concurrent adds under it, so two can't take the same code.
        var parent = await connection.QuerySingleOrDefaultAsync<ParentCodes>(new CommandDefinition("""
            SELECT ac_code AS AcCode FROM accounting.acc_ac_class WHERE acc_ac_class_id = @Id FOR UPDATE
            """, new { Id = acClassId }, transaction, cancellationToken: cancellationToken));
        if (parent is null)
            return null;

        var prefix = parent.AcCode?.Trim() ?? string.Empty;
        var code = prefix + await NextAsync(connection, transaction,
            "SELECT main_code FROM accounting.acc_main_class WHERE acc_ac_class_id = @Id", new { Id = acClassId },
            prefix, 2, AccountClassKind.Main, cancellationToken);
        var id = await connection.QuerySingleAsync<int>(new CommandDefinition("""
            INSERT INTO accounting.acc_main_class (ac_code, main_code, main_name, acc_ac_class_id)
            VALUES (@AcCode, @Code, @Name, @ParentId)
            RETURNING acc_main_class_id
            """, new { parent.AcCode, Code = code, Name = name, ParentId = acClassId }, transaction,
            cancellationToken: cancellationToken));
        return new AccountClassNode { Kind = AccountClassKind.Main, Id = id, Code = code, Name = name };
    }

    private static async Task<AccountClassNode?> CreateMapAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int mainClassId, string name, CancellationToken cancellationToken)
    {
        var parent = await connection.QuerySingleOrDefaultAsync<ParentCodes>(new CommandDefinition("""
            SELECT ac_code AS AcCode, main_code AS MainCode
            FROM accounting.acc_main_class WHERE acc_main_class_id = @Id FOR UPDATE
            """, new { Id = mainClassId }, transaction, cancellationToken: cancellationToken));
        if (parent is null)
            return null;

        // Map codes are numbered per main class, without its code in front: 0001, 0002, ...
        var code = await NextAsync(connection, transaction,
            "SELECT map_code FROM accounting.acc_map_class WHERE acc_main_class_id = @Id", new { Id = mainClassId },
            string.Empty, 4, AccountClassKind.Map, cancellationToken);
        var id = await connection.QuerySingleAsync<int>(new CommandDefinition("""
            INSERT INTO accounting.acc_map_class (map_code, map_name, prnt_code, ac_code, main_code, acc_main_class_id)
            VALUES (@Code, @Name, NULL, @AcCode, @MainCode, @ParentId)
            RETURNING acc_map_class_id
            """, new { Code = code, Name = name, parent.AcCode, parent.MainCode, ParentId = mainClassId }, transaction,
            cancellationToken: cancellationToken));
        return new AccountClassNode { Kind = AccountClassKind.Map, Id = id, Code = code, Name = name };
    }

    private static async Task<AccountClassNode?> CreateSubAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int mapClassId, string name, CancellationToken cancellationToken)
    {
        var parent = await connection.QuerySingleOrDefaultAsync<ParentCodes>(new CommandDefinition("""
            SELECT ac_code AS AcCode, main_code AS MainCode, map_code AS MapCode, acc_main_class_id AS MainId
            FROM accounting.acc_map_class WHERE acc_map_class_id = @Id FOR UPDATE
            """, new { Id = mapClassId }, transaction, cancellationToken: cancellationToken));
        if (parent is null)
            return null;

        // Sub codes are numbered across the whole main class, so its other map classes must wait too.
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT 1 FROM accounting.acc_main_class WHERE acc_main_class_id = @Id FOR UPDATE",
            new { Id = parent.MainId }, transaction, cancellationToken: cancellationToken));

        var prefix = parent.MainCode?.Trim() ?? string.Empty;
        var code = prefix + await NextAsync(connection, transaction,
            "SELECT sub_code FROM accounting.acc_sub_class WHERE main_code IS NOT DISTINCT FROM @MainCode",
            new { parent.MainCode }, prefix, 4, AccountClassKind.Sub, cancellationToken);
        // sl: the sub class's serial within its map class.
        var serial = await NextAsync(connection, transaction,
            "SELECT sl FROM accounting.acc_sub_class WHERE acc_map_class_id = @Id", new { Id = mapClassId },
            string.Empty, 3, AccountClassKind.Sub, cancellationToken);
        var id = await connection.QuerySingleAsync<int>(new CommandDefinition("""
            INSERT INTO accounting.acc_sub_class (ac_code, main_code, sub_code, sub_name, sl, map_code, acc_map_class_id)
            VALUES (@AcCode, @MainCode, @Code, @Name, @Serial, @MapCode, @ParentId)
            RETURNING acc_sub_class_id
            """, new { parent.AcCode, parent.MainCode, Code = code, Name = name, Serial = serial, parent.MapCode, ParentId = mapClassId },
            transaction, cancellationToken: cancellationToken));
        return new AccountClassNode { Kind = AccountClassKind.Sub, Id = id, Code = code, Name = name };
    }

    /// <summary>
    /// One more than the highest number after the prefix among the codes (codes that don't start with the prefix
    /// or don't end in a number are skipped), zero padded to the width.
    /// </summary>
    private static async Task<string> NextAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string codesSql, object parameters, string prefix,
        int width, string kind, CancellationToken cancellationToken)
    {
        var codes = await connection.QueryAsync<string?>(new CommandDefinition(codesSql, parameters, transaction,
            cancellationToken: cancellationToken));
        var last = 0L;
        foreach (var code in codes)
        {
            var value = code?.Trim();
            if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            if (long.TryParse(value.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > last)
                last = number;
        }

        var next = (last + 1).ToString(CultureInfo.InvariantCulture);
        if (next.Length > width)
            throw new BusinessConflictException($"There is no free {width}-digit number left for a new {AccountClassKind.Label(kind)} here.");
        return next.PadLeft(width, '0');
    }

    public async Task<AccountClassNode?> RenameAsync(string kind, int id, string name, CancellationToken cancellationToken = default)
    {
        var level = Levels[kind];
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition($"""
            UPDATE {level.Table} SET {level.Name} = @Name WHERE {level.Key} = @Id
            RETURNING {level.Key} AS Id, {level.Code} AS Code, {level.Name} AS Name
            """, new { Id = id, Name = name }, cancellationToken: cancellationToken));
        return row is null ? null : Node(kind, row, null);
    }

    public async Task<bool> HasChildrenAsync(string kind, int id, CancellationToken cancellationToken = default)
    {
        var level = Levels[kind];
        if (level.ChildTable is null)
            return false;
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleAsync<bool>(new CommandDefinition(
            $"SELECT EXISTS (SELECT 1 FROM {level.ChildTable} WHERE {level.ChildKey} = @Id)",
            new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<DeleteOutcome> DeleteAsync(string kind, int id, CancellationToken cancellationToken = default)
    {
        var level = Levels[kind];
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        // A voucher line still pointing at it (acc_voucher_detail → main class) answers InUse.
        return await RepositoryHelpers.DeleteAsync(connection, new CommandDefinition(
            $"DELETE FROM {level.Table} WHERE {level.Key} = @Id", new { Id = id }, cancellationToken: cancellationToken));
    }
}
