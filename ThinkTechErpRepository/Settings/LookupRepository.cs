using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Settings;

// public.lookup_* live in the same database as the security schema. Every query names the
// schema, so the connection's search path doesn't matter.
public sealed class LookupRepository(IDbConnectionFactory connectionFactory) : ILookupRepository
{
    private const string TableSelect = """
        SELECT t.lookup_table_id AS Id,
               t.lookup_table_name AS Name,
               t.is_active AS IsActive,
               (SELECT count(*) FROM public.lookup_data d WHERE d.lookup_table_id = t.lookup_table_id)::int AS DataCount,
               t.created_at AT TIME ZONE 'utc' AS CreatedAt,
               t.created_by AS CreatedBy,
               t.updated_at AT TIME ZONE 'utc' AS UpdatedAt,
               t.updated_by AS UpdatedBy
        FROM public.lookup_table t
        """;

    private const string TableFilter = """
        WHERE (CAST(@Pattern AS text) IS NULL OR t.lookup_table_name ILIKE @Pattern)
          AND (CAST(@IsActive AS boolean) IS NULL OR t.is_active = @IsActive)
        """;

    private const string DataSelect = """
        SELECT d.lookup_data_id AS Id,
               d.lookup_table_id AS LookupTableId,
               t.lookup_table_name AS LookupTableName,
               d.lookup_data_name AS Name,
               d.is_active AS IsActive,
               d.created_at AT TIME ZONE 'utc' AS CreatedAt,
               d.created_by AS CreatedBy,
               d.updated_at AT TIME ZONE 'utc' AS UpdatedAt,
               d.updated_by AS UpdatedBy
        FROM public.lookup_data d
        JOIN public.lookup_table t ON t.lookup_table_id = d.lookup_table_id
        """;

    private const string DataFilter = """
        WHERE (CAST(@LookupTableId AS bigint) IS NULL OR d.lookup_table_id = @LookupTableId)
          AND (CAST(@Pattern AS text) IS NULL OR d.lookup_data_name ILIKE @Pattern)
          AND (CAST(@IsActive AS boolean) IS NULL OR d.is_active = @IsActive)
        """;

    public async Task<PagedResult<LookupTable>> GetTablesAsync(
        string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default)
    {
        var parameters = new
        {
            Pattern = search is null ? null : SecurityDb.ContainsPattern(search),
            IsActive = isActive,
            Limit = limit,
            Offset = offset
        };

        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT count(*) FROM public.lookup_table t {TableFilter};
            {TableSelect} {TableFilter} ORDER BY t.lookup_table_name, t.lookup_table_id LIMIT @Limit OFFSET @Offset;
            """, parameters, cancellationToken: cancellationToken));
        var totalCount = await results.ReadSingleAsync<long>();
        var items = (await results.ReadAsync<LookupTable>()).AsList();
        return new PagedResult<LookupTable> { Items = items, TotalCount = (int)totalCount, Limit = limit, Offset = offset };
    }

    public async Task<LookupTable?> GetTableAsync(long lookupTableId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleOrDefaultAsync<LookupTable>(new CommandDefinition(
            $"{TableSelect} WHERE t.lookup_table_id = @LookupTableId", new { LookupTableId = lookupTableId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> TableNameExistsAsync(
        string name, long? excludeLookupTableId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1 FROM public.lookup_table
                WHERE lower(lookup_table_name) = lower(@Name)
                  AND (CAST(@ExcludeId AS bigint) IS NULL OR lookup_table_id <> @ExcludeId))
            """, new { Name = name, ExcludeId = excludeLookupTableId }, cancellationToken: cancellationToken));
    }

    public async Task<long> CreateTableAsync(
        LookupTableUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleAsync<long>(new CommandDefinition($"""
            INSERT INTO public.lookup_table (lookup_table_name, is_active, created_at, created_by)
            VALUES (@Name, @IsActive, {SecurityDb.NowUtc}, @UserId)
            RETURNING lookup_table_id
            """, new { request.Name, request.IsActive, UserId = userId }, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateTableAsync(
        long lookupTableId, LookupTableUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.lookup_table
            SET lookup_table_name = @Name,
                is_active = @IsActive,
                updated_at = {SecurityDb.NowUtc},
                updated_by = @UserId
            WHERE lookup_table_id = @LookupTableId
            """, new { LookupTableId = lookupTableId, request.Name, request.IsActive, UserId = userId },
            cancellationToken: cancellationToken)) > 0;
    }

    public async Task<DeleteOutcome> DeleteTableAsync(long lookupTableId, CancellationToken cancellationToken = default)
    {
        // A table that still has values is kept (ON DELETE RESTRICT on lookup_data).
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await DeleteAsync(connection, new CommandDefinition(
            "DELETE FROM public.lookup_table WHERE lookup_table_id = @LookupTableId",
            new { LookupTableId = lookupTableId }, cancellationToken: cancellationToken));
    }

    public async Task<PagedResult<LookupData>> GetDataAsync(
        long? lookupTableId, string? search, bool? isActive, int limit, int offset,
        CancellationToken cancellationToken = default)
    {
        var parameters = new
        {
            LookupTableId = lookupTableId,
            Pattern = search is null ? null : SecurityDb.ContainsPattern(search),
            IsActive = isActive,
            Limit = limit,
            Offset = offset
        };

        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT count(*) FROM public.lookup_data d {DataFilter};
            {DataSelect} {DataFilter}
            ORDER BY t.lookup_table_name, d.lookup_data_name, d.lookup_data_id
            LIMIT @Limit OFFSET @Offset;
            """, parameters, cancellationToken: cancellationToken));
        var totalCount = await results.ReadSingleAsync<long>();
        var items = (await results.ReadAsync<LookupData>()).AsList();
        return new PagedResult<LookupData> { Items = items, TotalCount = (int)totalCount, Limit = limit, Offset = offset };
    }

    public async Task<LookupData?> GetDataItemAsync(long lookupDataId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleOrDefaultAsync<LookupData>(new CommandDefinition(
            $"{DataSelect} WHERE d.lookup_data_id = @LookupDataId", new { LookupDataId = lookupDataId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> DataNameExistsAsync(
        long lookupTableId, string name, long? excludeLookupDataId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1 FROM public.lookup_data
                WHERE lookup_table_id = @LookupTableId
                  AND lower(lookup_data_name) = lower(@Name)
                  AND (CAST(@ExcludeId AS bigint) IS NULL OR lookup_data_id <> @ExcludeId))
            """, new { LookupTableId = lookupTableId, Name = name, ExcludeId = excludeLookupDataId },
            cancellationToken: cancellationToken));
    }

    public async Task<long> CreateDataAsync(
        LookupDataUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleAsync<long>(new CommandDefinition($"""
            INSERT INTO public.lookup_data (lookup_table_id, lookup_data_name, is_active, created_at, created_by)
            VALUES (@LookupTableId, @Name, @IsActive, {SecurityDb.NowUtc}, @UserId)
            RETURNING lookup_data_id
            """, new { request.LookupTableId, request.Name, request.IsActive, UserId = userId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateDataAsync(
        long lookupDataId, LookupDataUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.lookup_data
            SET lookup_table_id = @LookupTableId,
                lookup_data_name = @Name,
                is_active = @IsActive,
                updated_at = {SecurityDb.NowUtc},
                updated_by = @UserId
            WHERE lookup_data_id = @LookupDataId
            """, new { LookupDataId = lookupDataId, request.LookupTableId, request.Name, request.IsActive, UserId = userId },
            cancellationToken: cancellationToken)) > 0;
    }

    public async Task<DeleteOutcome> DeleteDataAsync(long lookupDataId, CancellationToken cancellationToken = default)
    {
        // A value still referenced elsewhere (e.g. a subscription's currency) is kept.
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await DeleteAsync(connection, new CommandDefinition(
            "DELETE FROM public.lookup_data WHERE lookup_data_id = @LookupDataId",
            new { LookupDataId = lookupDataId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<LookupOption>> GetOptionsAsync(
        string lookupTableName, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        var rows = await connection.QueryAsync<LookupOption>(new CommandDefinition("""
            SELECT d.lookup_data_id AS Id, d.lookup_data_name AS Name
            FROM public.lookup_data d
            JOIN public.lookup_table t ON t.lookup_table_id = d.lookup_table_id
            WHERE lower(t.lookup_table_name) = lower(@LookupTableName) AND t.is_active AND d.is_active
            ORDER BY d.lookup_data_name
            """, new { LookupTableName = lookupTableName }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    private static async Task<DeleteOutcome> DeleteAsync(NpgsqlConnection connection, CommandDefinition command)
    {
        try
        {
            return await connection.ExecuteAsync(command) > 0 ? DeleteOutcome.Deleted : DeleteOutcome.NotFound;
        }
        // ON DELETE RESTRICT (lookup_data -> lookup_table) reports 23001; plain foreign keys
        // (e.g. subscription -> lookup_data) report 23503.
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.ForeignKeyViolation
                                                                      or PostgresErrorCodes.RestrictViolation)
        {
            return DeleteOutcome.InUse;
        }
    }
}
