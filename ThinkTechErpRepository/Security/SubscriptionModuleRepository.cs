using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Database;

namespace ThinkTechErpRepository.Security;

public sealed class SubscriptionModuleRepository(IDbConnectionFactory connectionFactory) : ISubscriptionModuleRepository
{
    private const string ModuleSelect = """
        SELECT m.subscription_module_id AS Id,
               m.object_id AS ObjectId,
               m.module_name AS ModuleName,
               m.is_active AS IsActive,
               m.remarks AS Remarks,
               (SELECT count(*) FROM security.subscription s
                WHERE s.subscription_module_id = m.subscription_module_id)::int AS SubscriptionCount,
               m.created_at AT TIME ZONE 'utc' AS CreatedAt,
               m.created_by AS CreatedBy,
               m.updated_at AT TIME ZONE 'utc' AS UpdatedAt,
               m.updated_by AS UpdatedBy
        FROM security.subscription_module m
        """;

    private const string ModuleFilter = """
        WHERE (CAST(@Pattern AS text) IS NULL OR m.module_name ILIKE @Pattern OR m.remarks ILIKE @Pattern)
          AND (CAST(@IsActive AS boolean) IS NULL OR m.is_active = @IsActive)
        """;

    public async Task<PagedResult<SubscriptionModule>> GetModulesAsync(
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
            SELECT count(*) FROM security.subscription_module m {ModuleFilter};
            {ModuleSelect} {ModuleFilter} ORDER BY m.module_name, m.subscription_module_id LIMIT @Limit OFFSET @Offset;
            """, parameters, cancellationToken: cancellationToken));
        var totalCount = await results.ReadSingleAsync<long>();
        var items = (await results.ReadAsync<SubscriptionModule>()).AsList();
        return new PagedResult<SubscriptionModule> { Items = items, TotalCount = (int)totalCount, Limit = limit, Offset = offset };
    }

    public async Task<SubscriptionModule?> GetModuleAsync(int subscriptionModuleId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleOrDefaultAsync<SubscriptionModule>(new CommandDefinition(
            $"{ModuleSelect} WHERE m.subscription_module_id = @SubscriptionModuleId",
            new { SubscriptionModuleId = subscriptionModuleId }, cancellationToken: cancellationToken));
    }

    public async Task<bool> NameExistsAsync(
        string moduleName, int? excludeSubscriptionModuleId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1 FROM security.subscription_module
                WHERE lower(module_name) = lower(@ModuleName)
                  AND (CAST(@ExcludeId AS integer) IS NULL OR subscription_module_id <> @ExcludeId))
            """, new { ModuleName = moduleName, ExcludeId = excludeSubscriptionModuleId },
            cancellationToken: cancellationToken));
    }

    public async Task<int> CreateModuleAsync(
        SubscriptionModuleUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleAsync<int>(new CommandDefinition($"""
            INSERT INTO security.subscription_module (module_name, is_active, remarks, created_at, created_by)
            VALUES (@ModuleName, @IsActive, @Remarks, {SecurityDb.NowUtc}, @UserId)
            RETURNING subscription_module_id
            """, new { request.ModuleName, request.IsActive, request.Remarks, UserId = userId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateModuleAsync(
        int subscriptionModuleId, SubscriptionModuleUpsertRequest request, int userId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE security.subscription_module
            SET module_name = @ModuleName,
                is_active = @IsActive,
                remarks = @Remarks,
                updated_at = {SecurityDb.NowUtc},
                updated_by = @UserId
            WHERE subscription_module_id = @SubscriptionModuleId
            """, new
            {
                SubscriptionModuleId = subscriptionModuleId,
                request.ModuleName,
                request.IsActive,
                request.Remarks,
                UserId = userId
            }, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<DeleteOutcome> DeleteModuleAsync(int subscriptionModuleId, CancellationToken cancellationToken = default)
    {
        // A module that organizations subscribe to is kept; deactivate it instead.
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        try
        {
            var deleted = await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM security.subscription_module WHERE subscription_module_id = @SubscriptionModuleId",
                new { SubscriptionModuleId = subscriptionModuleId }, cancellationToken: cancellationToken));
            return deleted > 0 ? DeleteOutcome.Deleted : DeleteOutcome.NotFound;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return DeleteOutcome.InUse;
        }
    }
}
