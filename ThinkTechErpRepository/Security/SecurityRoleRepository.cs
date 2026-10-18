using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Database;

namespace ThinkTechErpRepository.Security;

public sealed class SecurityRoleRepository(IDbConnectionFactory connectionFactory) : ISecurityRoleRepository
{
    private const string RoleSelect = """
        SELECT r.id AS Id,
               r.name AS Name,
               r.description AS Description,
               (SELECT count(*) FROM security."UserRole" ur WHERE ur."roleId" = r.id)::int AS UserCount
        FROM security."Role" r
        """;

    private const string RolePermissionSelect = """
        SELECT rp."roleId" AS RoleId,
               p.id AS Id,
               p.code AS Code,
               p.name AS Name,
               p.description AS Description
        FROM security."RolePermission" rp
        JOIN security."Permission" p ON p.id = rp."permissionId"
        """;

    public async Task<IReadOnlyList<SecurityRole>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            {RoleSelect} ORDER BY r.name;
            {RolePermissionSelect} ORDER BY p.code;
            """, cancellationToken: cancellationToken));
        var roles = (await results.ReadAsync<SecurityRole>()).AsList();
        AttachPermissions(roles, await results.ReadAsync<RolePermissionRow>());
        return roles;
    }

    public async Task<SecurityRole?> GetRoleAsync(int roleId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            {RoleSelect} WHERE r.id = @RoleId;
            {RolePermissionSelect} WHERE rp."roleId" = @RoleId ORDER BY p.code;
            """, new { RoleId = roleId }, cancellationToken: cancellationToken));
        var role = await results.ReadSingleOrDefaultAsync<SecurityRole>();
        if (role is not null)
            AttachPermissions([role], await results.ReadAsync<RolePermissionRow>());

        return role;
    }

    public async Task<bool> NameExistsAsync(string name, int? excludeRoleId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1 FROM security."Role"
                WHERE lower(name) = lower(@Name)
                  AND (CAST(@ExcludeRoleId AS integer) IS NULL OR id <> @ExcludeRoleId))
            """, new { Name = name, ExcludeRoleId = excludeRoleId }, cancellationToken: cancellationToken));
    }

    public async Task<int> CreateRoleAsync(RoleUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var roleId = await connection.QuerySingleAsync<int>(new CommandDefinition("""
            INSERT INTO security."Role" (name, description)
            VALUES (@Name, @Description)
            RETURNING id
            """, new { request.Name, request.Description }, transaction, cancellationToken: cancellationToken));
        await ReplacePermissionsAsync(connection, transaction, roleId, request.PermissionIds ?? [], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return roleId;
    }

    public async Task<bool> UpdateRoleAsync(int roleId, RoleUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE security."Role"
            SET name = @Name, description = @Description
            WHERE id = @RoleId
            """, new { RoleId = roleId, request.Name, request.Description }, transaction,
            cancellationToken: cancellationToken));
        if (updated == 0)
            return false;

        if (request.PermissionIds is not null)
            await ReplacePermissionsAsync(connection, transaction, roleId, request.PermissionIds, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<DeleteOutcome> DeleteRoleAsync(int roleId, CancellationToken cancellationToken = default)
    {
        // A role still assigned to users is kept: deleting it would silently strip their access.
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        var deleted = await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM security."Role"
            WHERE id = @RoleId
              AND NOT EXISTS (SELECT 1 FROM security."UserRole" WHERE "roleId" = @RoleId)
            """, new { RoleId = roleId }, cancellationToken: cancellationToken));
        if (deleted > 0)
            return DeleteOutcome.Deleted;

        var exists = await connection.QuerySingleAsync<bool>(new CommandDefinition(
            """SELECT EXISTS (SELECT 1 FROM security."Role" WHERE id = @RoleId)""",
            new { RoleId = roleId }, cancellationToken: cancellationToken));
        return exists ? DeleteOutcome.InUse : DeleteOutcome.NotFound;
    }

    private static Task ReplacePermissionsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int roleId, int[] permissionIds,
        CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM security."RolePermission" WHERE "roleId" = @RoleId;
            INSERT INTO security."RolePermission" ("roleId", "permissionId")
            SELECT DISTINCT @RoleId, permission_id
            FROM unnest(CAST(@PermissionIds AS integer[])) AS t(permission_id);
            """, new { RoleId = roleId, PermissionIds = permissionIds }, transaction,
            cancellationToken: cancellationToken));

    private static void AttachPermissions(IReadOnlyList<SecurityRole> roles, IEnumerable<RolePermissionRow> rows)
    {
        var permissionsByRole = rows.ToLookup(row => row.RoleId, row => new SecurityPermission
        {
            Id = row.Id,
            Code = row.Code,
            Name = row.Name,
            Description = row.Description
        });
        foreach (var role in roles)
            role.Permissions = permissionsByRole[role.Id].ToArray();
    }

    private sealed class RolePermissionRow
    {
        public int RoleId { get; set; }
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }
}
