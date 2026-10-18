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
        FROM security."Permission"
        """;

    public async Task<IReadOnlyList<SecurityPermission>> GetPermissionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        var rows = await connection.QueryAsync<SecurityPermission>(new CommandDefinition(
            $"{PermissionSelect} ORDER BY code", cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<SecurityPermission?> GetPermissionAsync(int permissionId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleOrDefaultAsync<SecurityPermission>(new CommandDefinition(
            $"{PermissionSelect} WHERE id = @PermissionId", new { PermissionId = permissionId },
            cancellationToken: cancellationToken));
    }

    public async Task<int> CreatePermissionAsync(PermissionUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleAsync<int>(new CommandDefinition("""
            INSERT INTO security."Permission" (code, name, description)
            VALUES (@Code, @Name, @Description)
            RETURNING id
            """, request, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdatePermissionAsync(
        int permissionId, PermissionUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE security."Permission"
            SET code = @Code, name = @Name, description = @Description
            WHERE id = @PermissionId
            """, new { PermissionId = permissionId, request.Code, request.Name, request.Description },
            cancellationToken: cancellationToken)) > 0;
    }

    public async Task<DeleteOutcome> DeletePermissionAsync(int permissionId, CancellationToken cancellationToken = default)
    {
        // A permission still granted to a role or user is kept: deleting it would silently revoke access.
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        var deleted = await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM security."Permission"
            WHERE id = @PermissionId
              AND NOT EXISTS (SELECT 1 FROM security."RolePermission" WHERE "permissionId" = @PermissionId)
              AND NOT EXISTS (SELECT 1 FROM security."UserPermission" WHERE "permissionId" = @PermissionId)
            """, new { PermissionId = permissionId }, cancellationToken: cancellationToken));
        if (deleted > 0)
            return DeleteOutcome.Deleted;

        var exists = await connection.QuerySingleAsync<bool>(new CommandDefinition(
            """SELECT EXISTS (SELECT 1 FROM security."Permission" WHERE id = @PermissionId)""",
            new { PermissionId = permissionId }, cancellationToken: cancellationToken));
        return exists ? DeleteOutcome.InUse : DeleteOutcome.NotFound;
    }
}
