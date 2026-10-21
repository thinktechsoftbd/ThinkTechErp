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

    // udt_schema/udt_name name the column's base type without its modifiers (e.g. pg_catalog.varchar,
    // pg_catalog._text for text[]), so a cast to it keeps length and precision checks on assignment.
    private const string WritableColumnsSql = """
        SELECT column_name AS ColumnName,
               data_type AS DataType,
               udt_schema AS UdtSchema,
               udt_name AS UdtName
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
            parameterNames.Add(columns[index].CastParameter(parameterName));
            parameters.Add(parameterName, ToDatabaseValue(record[columns[index].ColumnName], columns[index]));
        }

        var sql = $"INSERT INTO {tableSql} AS t ({string.Join(", ", columns.Select(column => Quote(column.ColumnName)))}) " +
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
        if (columns.Any(column => string.Equals(column.ColumnName, key, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"The primary key field '{key}' cannot be changed.", nameof(record));
        }

        var parameters = new DynamicParameters();
        parameters.Add("Id", id);
        var assignments = new List<string>();
        for (var index = 0; index < columns.Count; index++)
        {
            var parameterName = $"p{index}";
            assignments.Add($"{Quote(columns[index].ColumnName)} = {columns[index].CastParameter(parameterName)}");
            parameters.Add(parameterName, ToDatabaseValue(record[columns[index].ColumnName], columns[index]));
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

    private static async Task<IReadOnlyList<WritableColumn>> GetValidatedColumnsAsync(
        Npgsql.NpgsqlConnection connection,
        string table,
        WebsiteRecord record,
        CancellationToken cancellationToken)
    {
        if (record.Count == 0)
        {
            throw new ArgumentException("Provide at least one record field.", nameof(record));
        }

        var writableColumns = (await connection.QueryAsync<WritableColumn>(
            new CommandDefinition(
                WritableColumnsSql,
                new { Schema, Table = table },
                cancellationToken: cancellationToken))).ToDictionary(column => column.ColumnName, StringComparer.Ordinal);

        var unknownColumns = record.Keys.Where(column => !writableColumns.ContainsKey(column)).ToArray();
        if (unknownColumns.Length > 0)
        {
            throw new ArgumentException(
                $"Unknown or read-only field(s) for '{table}': {string.Join(", ", unknownColumns)}.",
                nameof(record));
        }

        return record.Keys.Select(column => writableColumns[column]).ToArray();
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

    // Every value is cast to its column's type in SQL (see WritableColumn.CastParameter), so strings
    // reach date/timestamptz/uuid columns and null stays typed. json/jsonb columns get the raw JSON
    // text (a JSON string stays a JSON string); JSON arrays for array columns are sent as text[].
    private static object? ToDatabaseValue(JsonElement value, WritableColumn column) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ when column.IsJson => value.GetRawText(),
        JsonValueKind.Array when column.IsArray => value.EnumerateArray().Select(ToArrayElement).ToArray(),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
        JsonValueKind.Number => value.GetDouble(),
        _ => value.GetRawText()
    };

    private static string? ToArrayElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => element.GetString(),
        _ => element.GetRawText()
    };

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    private sealed class WritableColumn
    {
        public string ColumnName { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public string UdtSchema { get; set; } = string.Empty;
        public string UdtName { get; set; } = string.Empty;

        public bool IsArray => DataType == "ARRAY";
        public bool IsJson => UdtSchema == "pg_catalog" && UdtName is "json" or "jsonb";

        public string CastParameter(string parameterName) =>
            $"CAST(@{parameterName} AS {Quote(UdtSchema)}.{Quote(UdtName)})";
    }
}
