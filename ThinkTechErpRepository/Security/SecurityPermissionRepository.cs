using Dapper;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Database;

namespace ThinkTechErpRepository.Security;

public sealed class SecurityPermissionRepository(IDbConnectionFactory connectionFactory) : ISecurityPermissionRepository
{
    private const string PermissionSelect = """
        SELECT id AS Id,
               code AS Code,
               name AS Name,
               description AS Description
        FROM public."Permission"
        """;

    public async Task<IReadOnlyList<SecurityPermission>> GetPermissionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<SecurityPermission>(new CommandDefinition(
            $"{PermissionSelect} ORDER BY code", cancellationToken: cancellationToken));
        return rows.AsList();
    }

    // The columns a page may be sorted by (never interpolate anything else into ORDER BY).
    private static readonly Dictionary<string, string> SortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["code"] = "code",
        ["name"] = "name",
        ["description"] = "description"
    };

    public async Task<PagedResult<SecurityPermission>> SearchPermissionsAsync(
        string? search, string sort, bool descending, int limit, int offset, CancellationToken cancellationToken = default)
    {
        var column = SortColumns.GetValueOrDefault(sort, "code");
        var direction = descending ? "DESC" : "ASC";
        const string filter = """
            WHERE CAST(@Pattern AS text) IS NULL OR code ILIKE @Pattern OR name ILIKE @Pattern
            """;
        await using var connection = connectionFactory.CreateConnection();
        await using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT count(*) FROM public."Permission" {filter};
            {PermissionSelect} {filter}
            ORDER BY {column} {direction} NULLS LAST, id
            LIMIT @Limit OFFSET @Offset;
            """, new { Pattern = search is null ? null : SecurityDb.ContainsPattern(search), Limit = limit, Offset = offset },
            cancellationToken: cancellationToken));
        var total = await results.ReadSingleAsync<long>();
        var items = (await results.ReadAsync<SecurityPermission>()).AsList();
        return new PagedResult<SecurityPermission> { Items = items, TotalCount = (int)total, Limit = limit, Offset = offset };
    }

    public async Task<SecurityPermission?> GetPermissionAsync(int permissionId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<SecurityPermission>(new CommandDefinition(
            $"{PermissionSelect} WHERE id = @PermissionId", new { PermissionId = permissionId },
            cancellationToken: cancellationToken));
    }

    public async Task<int> CreatePermissionAsync(PermissionUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<int>(new CommandDefinition("""
            INSERT INTO public."Permission" (code, name, description)
            VALUES (@Code, @Name, @Description)
            RETURNING id
            """, request, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdatePermissionAsync(
        int permissionId, PermissionUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE public."Permission"
            SET code = @Code, name = @Name, description = @Description
            WHERE id = @PermissionId
            """, new { PermissionId = permissionId, request.Code, request.Name, request.Description },
            cancellationToken: cancellationToken)) > 0;
    }

    public async Task<DeleteOutcome> DeletePermissionAsync(int permissionId, CancellationToken cancellationToken = default)
    {
        // A permission still granted to a role or user is kept: deleting it would silently revoke access.
        await using var connection = connectionFactory.CreateConnection();
        var deleted = await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM public."Permission"
            WHERE id = @PermissionId
              AND NOT EXISTS (SELECT 1 FROM public."RolePermission" WHERE "permissionId" = @PermissionId)
              AND NOT EXISTS (SELECT 1 FROM public."UserPermission" WHERE "permissionId" = @PermissionId)
            """, new { PermissionId = permissionId }, cancellationToken: cancellationToken));
        if (deleted > 0)
            return DeleteOutcome.Deleted;

        var exists = await connection.QuerySingleAsync<bool>(new CommandDefinition(
            """SELECT EXISTS (SELECT 1 FROM public."Permission" WHERE id = @PermissionId)""",
            new { PermissionId = permissionId }, cancellationToken: cancellationToken));
        return exists ? DeleteOutcome.InUse : DeleteOutcome.NotFound;
    }
}
