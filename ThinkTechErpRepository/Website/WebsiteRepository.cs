using System.Text.Json;
using Dapper;
using ThinkTechErpDomain.Models;
using ThinkTechErpRepository.Database;

namespace ThinkTechErpRepository.Website;

public sealed class WebsiteRepository(IDbConnectionFactory connectionFactory) : IWebsiteRepository
{
    private const string Schema = "website";
    private static readonly HashSet<string> SupportedTables = new(StringComparer.Ordinal)
    {
        "testimonials", "case_studies", "faqs", "industries", "inquiries",
        "job_applications", "job_openings", "newsletter_subscribers", "posts", "services", "stats"
    };

    private const string WritableColumnsSql = """
        SELECT column_name
        FROM information_schema.columns
        WHERE table_schema = @Schema
          AND table_name = @Table
          AND is_identity <> 'YES'
          AND is_generated = 'NEVER'
        ORDER BY ordinal_position
        """;

    private const string PrimaryKeySql = """
        SELECT a.attname
        FROM pg_index i
        JOIN pg_attribute a ON a.attrelid = i.indrelid
        JOIN unnest(i.indkey) WITH ORDINALITY AS k(attnum, ord) ON k.attnum = a.attnum
        WHERE i.indrelid = to_regclass(@QualifiedTable)
          AND i.indisprimary
        ORDER BY k.ord
        """;

    public async Task<IReadOnlyList<WebsiteRecord>> GetAllAsync(
        string table,
        CancellationToken cancellationToken = default)
    {
        var tableSql = GetQualifiedTable(table);
        await using var connection = connectionFactory.CreateConnection("Website");
        var jsonRows = await connection.QueryAsync<string>(
            new CommandDefinition(
                $"SELECT to_jsonb(t)::text FROM {tableSql} AS t",
                cancellationToken: cancellationToken));

        return jsonRows.Select(WebsiteRecord.FromJson).ToArray();
    }

    public async Task<WebsiteRecord?> GetByIdAsync(
        string table,
        string id,
        CancellationToken cancellationToken = default)
    {
        var tableSql = GetQualifiedTable(table);
        await using var connection = connectionFactory.CreateConnection("Website");
        var key = await GetPrimaryKeyAsync(connection, table, cancellationToken);
        var json = await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(
                $"SELECT to_jsonb(t)::text FROM {tableSql} AS t WHERE t.{Quote(key)}::text = @Id",
                new { Id = id },
                cancellationToken: cancellationToken));

        return json is null ? null : WebsiteRecord.FromJson(json);
    }

    public async Task<WebsiteRecord> CreateAsync(
        string table,
        WebsiteRecord record,
        CancellationToken cancellationToken = default)
    {
        var tableSql = GetQualifiedTable(table);
        await using var connection = connectionFactory.CreateConnection("Website");
        var columns = await GetValidatedColumnsAsync(connection, table, record, cancellationToken);
        var parameters = new DynamicParameters();
        var parameterNames = new List<string>();
        for (var index = 0; index < columns.Count; index++)
        {
            var parameterName = $"p{index}";
            parameterNames.Add($"@{parameterName}");
            parameters.Add(parameterName, ToDatabaseValue(record[columns[index]]));
        }

        var sql = $"INSERT INTO {tableSql} AS t ({string.Join(", ", columns.Select(Quote))}) " +
                  $"VALUES ({string.Join(", ", parameterNames)}) RETURNING to_jsonb(t)::text";
        var json = await connection.QuerySingleAsync<string>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        return WebsiteRecord.FromJson(json);
    }

    public async Task<WebsiteRecord?> UpdateAsync(
        string table,
        string id,
        WebsiteRecord record,
        CancellationToken cancellationToken = default)
    {
        var tableSql = GetQualifiedTable(table);
        await using var connection = connectionFactory.CreateConnection("Website");
        var key = await GetPrimaryKeyAsync(connection, table, cancellationToken);
        var columns = await GetValidatedColumnsAsync(connection, table, record, cancellationToken);
        if (columns.Contains(key, StringComparer.Ordinal))
        {
            throw new ArgumentException($"The primary key field '{key}' cannot be changed.", nameof(record));
        }

        var parameters = new DynamicParameters();
        parameters.Add("Id", id);
        var assignments = new List<string>();
        for (var index = 0; index < columns.Count; index++)
        {
            var parameterName = $"p{index}";
            assignments.Add($"{Quote(columns[index])} = @{parameterName}");
            parameters.Add(parameterName, ToDatabaseValue(record[columns[index]]));
        }

        var sql = $"UPDATE {tableSql} AS t SET {string.Join(", ", assignments)} " +
                  $"WHERE t.{Quote(key)}::text = @Id RETURNING to_jsonb(t)::text";
        var json = await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        return json is null ? null : WebsiteRecord.FromJson(json);
    }

    public async Task<bool> DeleteAsync(
        string table,
        string id,
        CancellationToken cancellationToken = default)
    {
        var tableSql = GetQualifiedTable(table);
        await using var connection = connectionFactory.CreateConnection("Website");
        var key = await GetPrimaryKeyAsync(connection, table, cancellationToken);
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                $"DELETE FROM {tableSql} WHERE {Quote(key)}::text = @Id",
                new { Id = id },
                cancellationToken: cancellationToken));

        return affected > 0;
    }

    private static string GetQualifiedTable(string table)
    {
        if (!SupportedTables.Contains(table))
        {
            throw new ArgumentException($"Unsupported website table '{table}'.", nameof(table));
        }

        return $"{Quote(Schema)}.{Quote(table)}";
    }

    private static async Task<IReadOnlyList<string>> GetValidatedColumnsAsync(
        Npgsql.NpgsqlConnection connection,
        string table,
        WebsiteRecord record,
        CancellationToken cancellationToken)
    {
        if (record.Count == 0)
        {
            throw new ArgumentException("Provide at least one record field.", nameof(record));
        }

        var writableColumns = (await connection.QueryAsync<string>(
            new CommandDefinition(
                WritableColumnsSql,
                new { Schema, Table = table },
                cancellationToken: cancellationToken))).ToHashSet(StringComparer.Ordinal);

        var unknownColumns = record.Keys.Where(column => !writableColumns.Contains(column)).ToArray();
        if (unknownColumns.Length > 0)
        {
            throw new ArgumentException(
                $"Unknown or read-only field(s) for '{table}': {string.Join(", ", unknownColumns)}.",
                nameof(record));
        }

        return record.Keys.ToArray();
    }

    private static async Task<string> GetPrimaryKeyAsync(
        Npgsql.NpgsqlConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        var keys = (await connection.QueryAsync<string>(
            new CommandDefinition(
                PrimaryKeySql,
                new { QualifiedTable = $"\"{Schema}\".\"{table}\"" },
                cancellationToken: cancellationToken))).AsList();

        return keys.Count switch
        {
            1 => keys[0],
            0 => throw new InvalidOperationException($"{Schema}.{table} has no primary key."),
            _ => throw new InvalidOperationException(
                $"{Schema}.{table} has a composite primary key; this API currently expects a single-column key.")
        };
    }

    private static object? ToDatabaseValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => DBNull.Value,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
        JsonValueKind.Number => value.GetDouble(),
        _ => value.Clone()
    };

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";
}
