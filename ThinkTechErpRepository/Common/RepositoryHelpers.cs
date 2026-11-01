using System.Data;
using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpRepository.Common;

internal static class RepositoryHelpers
{
    /// <summary>Runs "count; page" as one round trip and wraps the rows in a PagedResult.</summary>
    public static async Task<PagedResult<T>> PagedAsync<T>(
        IDbConnection connection, string countSql, string pageSql, object parameters, int limit, int offset,
        CancellationToken cancellationToken)
    {
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            $"{countSql};\n{pageSql} LIMIT @Limit OFFSET @Offset;", Merge(parameters, limit, offset),
            cancellationToken: cancellationToken));
        var total = await results.ReadSingleAsync<long>();
        var items = (await results.ReadAsync<T>()).AsList();
        return new PagedResult<T> { Items = items, TotalCount = (int)total, Limit = limit, Offset = offset };
    }

    private static DynamicParameters Merge(object parameters, int limit, int offset)
    {
        var merged = new DynamicParameters(parameters);
        merged.Add("Limit", limit);
        merged.Add("Offset", offset);
        return merged;
    }

    /// <summary>Deletes one row; a foreign key still pointing at it means it is in use.</summary>
    public static async Task<DeleteOutcome> DeleteAsync(IDbConnection connection, CommandDefinition command)
    {
        try
        {
            return await connection.ExecuteAsync(command) > 0 ? DeleteOutcome.Deleted : DeleteOutcome.NotFound;
        }
        // ON DELETE RESTRICT keys (e.g. the acc_* tables from MediDesk's Prisma schema) report 23001 instead of 23503.
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.RestrictViolation)
        {
            return DeleteOutcome.InUse;
        }
    }

    public static string? Pattern(string? search) => search is null ? null : Security.SecurityDb.ContainsPattern(search);
}
