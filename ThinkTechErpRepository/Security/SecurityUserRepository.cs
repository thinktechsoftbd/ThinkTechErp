using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Database;

namespace ThinkTechErpRepository.Security;

public sealed class SecurityUserRepository(IDbConnectionFactory connectionFactory) : ISecurityUserRepository
{
    private const string CredentialsSelect = """
        SELECT id AS Id,
               name AS Name,
               email AS Email,
               "photoUrl" AS PhotoUrl,
               "passwordHash" AS PasswordHash,
               status::text AS Status
        FROM security."User"
        """;

    private const string UserSelect = """
        SELECT u.id AS Id,
               u.name AS Name,
               u.email AS Email,
               u."photoUrl" AS PhotoUrl,
               u.status::text AS Status,
               u."createdAt" AT TIME ZONE 'utc' AS CreatedAt,
               u."updatedAt" AT TIME ZONE 'utc' AS UpdatedAt
        FROM security."User" u
        """;

    private const string UserFilter = """
        WHERE (CAST(@Pattern AS text) IS NULL OR u.name ILIKE @Pattern OR u.email ILIKE @Pattern)
          AND (CAST(@Status AS text) IS NULL OR u.status::text = @Status)
        """;

    // Permissions from the user's roles plus the ones granted to the user directly.
    private const string EffectivePermissionsSelect = """
        SELECT p.code
        FROM security."Permission" p
        WHERE p.id IN (
            SELECT rp."permissionId"
            FROM security."RolePermission" rp
            JOIN security."UserRole" ur ON ur."roleId" = rp."roleId"
            WHERE ur."userId" = @UserId
            UNION
            SELECT up."permissionId"
            FROM security."UserPermission" up
            WHERE up."userId" = @UserId)
        ORDER BY p.code
        """;

    public async Task<SecurityUserCredentials?> GetCredentialsByEmailAsync(
        string email, CancellationToken cancellationToken = default)
    {
        // Emails match regardless of case; an exact match wins if two accounts differ only by case.
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QueryFirstOrDefaultAsync<SecurityUserCredentials>(new CommandDefinition(
            $"{CredentialsSelect} WHERE lower(email) = lower(@Email) ORDER BY (email = @Email) DESC, id LIMIT 1",
            new { Email = email }, cancellationToken: cancellationToken));
    }

    public async Task<SecurityUserCredentials?> GetCredentialsByIdAsync(
        int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleOrDefaultAsync<SecurityUserCredentials>(new CommandDefinition(
            $"{CredentialsSelect} WHERE id = @UserId", new { UserId = userId }, cancellationToken: cancellationToken));
    }

    public async Task<UserAccess> GetAccessAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT r.name
            FROM security."UserRole" ur
            JOIN security."Role" r ON r.id = ur."roleId"
            WHERE ur."userId" = @UserId
            ORDER BY r.name;
            {EffectivePermissionsSelect};
            """, new { UserId = userId }, cancellationToken: cancellationToken));
        return new UserAccess
        {
            Roles = (await results.ReadAsync<string>()).ToArray(),
            Permissions = (await results.ReadAsync<string>()).ToArray()
        };
    }

    public async Task<IReadOnlyList<MenuNode>> GetVisibleMenusAsync(
        int userId, CancellationToken cancellationToken = default)
    {
        // A per-user UserMenu row decides visibility when present; otherwise the menu is shown
        // when it has no role or the user holds that role.
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        var rows = await connection.QueryAsync<MenuNode>(new CommandDefinition("""
            SELECT m.id AS Id,
                   m.title AS Title,
                   m.path AS Path,
                   m.icon AS Icon,
                   m."sortOrder" AS SortOrder,
                   m."parentId" AS ParentId,
                   um."canCreate" AS CanCreate,
                   um."canEdit" AS CanEdit,
                   um."canDelete" AS CanDelete
            FROM security."Menu" m
            LEFT JOIN security."UserMenu" um ON um."menuId" = m.id AND um."userId" = @UserId
            WHERE m."isActive"
              AND CASE
                      WHEN um."userId" IS NOT NULL THEN um."canView"
                      ELSE m."roleId" IS NULL
                           OR m."roleId" IN (SELECT ur."roleId" FROM security."UserRole" ur WHERE ur."userId" = @UserId)
                  END
            ORDER BY m."sortOrder", m.id
            """, new { UserId = userId }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<bool> UpdatePasswordHashAsync(
        int userId, string passwordHash, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE security."User"
            SET "passwordHash" = @PasswordHash, "updatedAt" = {SecurityDb.NowUtc}
            WHERE id = @UserId
            """, new { UserId = userId, PasswordHash = passwordHash }, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<PagedResult<SecurityUser>> GetUsersAsync(
        string? search, string? status, int limit, int offset, CancellationToken cancellationToken = default)
    {
        var parameters = new
        {
            Pattern = search is null ? null : SecurityDb.ContainsPattern(search),
            Status = status,
            Limit = limit,
            Offset = offset
        };

        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        long totalCount;
        List<SecurityUser> users;
        using (var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT count(*) FROM security."User" u {UserFilter};
            {UserSelect} {UserFilter} ORDER BY u.name, u.id LIMIT @Limit OFFSET @Offset;
            """, parameters, cancellationToken: cancellationToken)))
        {
            totalCount = await results.ReadSingleAsync<long>();
            users = (await results.ReadAsync<SecurityUser>()).AsList();
        }

        await AttachRolesAsync(connection, users, cancellationToken);
        return new PagedResult<SecurityUser> { Items = users, TotalCount = (int)totalCount, Limit = limit, Offset = offset };
    }

    public async Task<SecurityUser?> GetUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        var user = await connection.QuerySingleOrDefaultAsync<SecurityUser>(new CommandDefinition(
            $"{UserSelect} WHERE u.id = @UserId", new { UserId = userId }, cancellationToken: cancellationToken));
        if (user is not null)
            await AttachRolesAsync(connection, [user], cancellationToken);

        return user;
    }

    public async Task<bool> EmailExistsAsync(
        string email, int? excludeUserId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1 FROM security."User"
                WHERE lower(email) = lower(@Email)
                  AND (CAST(@ExcludeUserId AS integer) IS NULL OR id <> @ExcludeUserId))
            """, new { Email = email, ExcludeUserId = excludeUserId }, cancellationToken: cancellationToken));
    }

    public async Task<int> CreateUserAsync(
        UserCreateRequest request, string passwordHash, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var userId = await connection.QuerySingleAsync<int>(new CommandDefinition($"""
            INSERT INTO security."User" (name, email, "passwordHash", "photoUrl", status, "createdAt", "updatedAt")
            VALUES (@Name, @Email, @PasswordHash, @PhotoUrl, CAST(@Status AS security."UserStatus"),
                    {SecurityDb.NowUtc}, {SecurityDb.NowUtc})
            RETURNING id
            """, new { request.Name, request.Email, PasswordHash = passwordHash, request.PhotoUrl, request.Status },
            transaction, cancellationToken: cancellationToken));
        await ReplaceRolesAsync(connection, transaction, userId, request.RoleIds, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return userId;
    }

    public async Task<bool> UpdateUserAsync(
        int userId, UserUpdateRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var updated = await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE security."User"
            SET name = @Name,
                email = @Email,
                "photoUrl" = @PhotoUrl,
                status = CAST(@Status AS security."UserStatus"),
                "updatedAt" = {SecurityDb.NowUtc}
            WHERE id = @UserId
            """, new { UserId = userId, request.Name, request.Email, request.PhotoUrl, request.Status },
            transaction, cancellationToken: cancellationToken));
        if (updated == 0)
            return false;

        if (request.RoleIds is not null)
            await ReplaceRolesAsync(connection, transaction, userId, request.RoleIds, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<UserPermissionSet?> GetUserPermissionsAsync(
        int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT EXISTS (SELECT 1 FROM security."User" WHERE id = @UserId);
            SELECT p.id AS Id, p.code AS Code, p.name AS Name, p.description AS Description
            FROM security."UserPermission" up
            JOIN security."Permission" p ON p.id = up."permissionId"
            WHERE up."userId" = @UserId
            ORDER BY p.code;
            {EffectivePermissionsSelect};
            """, new { UserId = userId }, cancellationToken: cancellationToken));
        if (!await results.ReadSingleAsync<bool>())
            return null;

        return new UserPermissionSet
        {
            Direct = (await results.ReadAsync<SecurityPermission>()).ToArray(),
            Effective = (await results.ReadAsync<string>()).ToArray()
        };
    }

    public async Task<bool> SetUserPermissionsAsync(
        int userId, int[] permissionIds, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await UserExistsAsync(connection, transaction, userId, cancellationToken))
            return false;

        await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM security."UserPermission" WHERE "userId" = @UserId;
            INSERT INTO security."UserPermission" ("userId", "permissionId")
            SELECT DISTINCT @UserId, permission_id
            FROM unnest(CAST(@PermissionIds AS integer[])) AS t(permission_id);
            """, new { UserId = userId, PermissionIds = permissionIds }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<UserMenuAccess>?> GetUserMenusAsync(
        int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition("""
            SELECT EXISTS (SELECT 1 FROM security."User" WHERE id = @UserId);
            SELECT um."menuId" AS MenuId,
                   m.title AS Title,
                   m.path AS Path,
                   um."canView" AS CanView,
                   um."canCreate" AS CanCreate,
                   um."canEdit" AS CanEdit,
                   um."canDelete" AS CanDelete
            FROM security."UserMenu" um
            JOIN security."Menu" m ON m.id = um."menuId"
            WHERE um."userId" = @UserId
            ORDER BY m."sortOrder", m.id;
            """, new { UserId = userId }, cancellationToken: cancellationToken));
        if (!await results.ReadSingleAsync<bool>())
            return null;

        return (await results.ReadAsync<UserMenuAccess>()).AsList();
    }

    public async Task<bool> SetUserMenusAsync(
        int userId, UserMenuAccessItem[] menus, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await UserExistsAsync(connection, transaction, userId, cancellationToken))
            return false;

        await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM security."UserMenu" WHERE "userId" = @UserId;
            INSERT INTO security."UserMenu" ("userId", "menuId", "canView", "canCreate", "canEdit", "canDelete")
            SELECT @UserId, t.menu_id, t.can_view, t.can_create, t.can_edit, t.can_delete
            FROM unnest(CAST(@MenuIds AS integer[]), CAST(@CanView AS boolean[]), CAST(@CanCreate AS boolean[]),
                        CAST(@CanEdit AS boolean[]), CAST(@CanDelete AS boolean[]))
                 AS t(menu_id, can_view, can_create, can_edit, can_delete);
            """, new
            {
                UserId = userId,
                MenuIds = menus.Select(m => m.MenuId).ToArray(),
                CanView = menus.Select(m => m.CanView).ToArray(),
                CanCreate = menus.Select(m => m.CanCreate).ToArray(),
                CanEdit = menus.Select(m => m.CanEdit).ToArray(),
                CanDelete = menus.Select(m => m.CanDelete).ToArray()
            }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task<bool> UserExistsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int userId, CancellationToken cancellationToken)
        => await connection.QuerySingleAsync<bool>(new CommandDefinition(
            """SELECT EXISTS (SELECT 1 FROM security."User" WHERE id = @UserId)""",
            new { UserId = userId }, transaction, cancellationToken: cancellationToken));

    private static Task ReplaceRolesAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int userId, int[] roleIds,
        CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM security."UserRole" WHERE "userId" = @UserId;
            INSERT INTO security."UserRole" ("userId", "roleId")
            SELECT DISTINCT @UserId, role_id
            FROM unnest(CAST(@RoleIds AS integer[])) AS t(role_id);
            """, new { UserId = userId, RoleIds = roleIds }, transaction, cancellationToken: cancellationToken));

    private static async Task AttachRolesAsync(
        NpgsqlConnection connection, IReadOnlyList<SecurityUser> users, CancellationToken cancellationToken)
    {
        if (users.Count == 0)
            return;

        var rows = await connection.QueryAsync<UserRoleRow>(new CommandDefinition("""
            SELECT ur."userId" AS UserId, r.id AS Id, r.name AS Name
            FROM security."UserRole" ur
            JOIN security."Role" r ON r.id = ur."roleId"
            WHERE ur."userId" = ANY(@UserIds)
            ORDER BY r.name
            """, new { UserIds = users.Select(u => u.Id).ToArray() }, cancellationToken: cancellationToken));
        var rolesByUser = rows.ToLookup(row => row.UserId, row => new RoleSummary { Id = row.Id, Name = row.Name });
        foreach (var user in users)
            user.Roles = rolesByUser[user.Id].ToArray();
    }

    private sealed class UserRoleRow
    {
        public int UserId { get; set; }
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
