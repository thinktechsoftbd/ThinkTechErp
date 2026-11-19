using Dapper;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Database;

namespace ThinkTechErpRepository.Security;

public sealed class SecurityMenuRepository(IDbConnectionFactory connectionFactory) : ISecurityMenuRepository
{
    // A menu's roles: its MenuRole rows plus the legacy single "roleId" (set by older scripts), each once.
    private const string MenuSelect = """
        SELECT m.id AS Id,
               m.title AS Title,
               m.path AS Path,
               m.icon AS Icon,
               m."sortOrder" AS SortOrder,
               m."parentId" AS ParentId,
               ARRAY(SELECT mr."roleId" FROM public."MenuRole" mr WHERE mr."menuId" = m.id
                     UNION
                     SELECT m."roleId" WHERE m."roleId" IS NOT NULL
                     ORDER BY 1) AS RoleIds,
               m."isActive" AS IsActive
        FROM public."Menu" m
        """;

    public async Task<IReadOnlyList<SecurityMenu>> GetMenusAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<SecurityMenu>(new CommandDefinition(
            $"""{MenuSelect} ORDER BY m."parentId" NULLS FIRST, m."sortOrder", m.id""",
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<SecurityMenu?> GetMenuAsync(int menuId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<SecurityMenu>(new CommandDefinition(
            $"{MenuSelect} WHERE m.id = @MenuId", new { MenuId = menuId }, cancellationToken: cancellationToken));
    }

    // The legacy "roleId" is cleared on save: the menu's roles then live in MenuRole only.
    public async Task<int> CreateMenuAsync(MenuUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var menuId = await connection.QuerySingleAsync<int>(new CommandDefinition("""
            INSERT INTO public."Menu" (title, path, icon, "sortOrder", "parentId", "roleId", "isActive")
            VALUES (@Title, @Path, @Icon, @SortOrder, @ParentId, NULL, @IsActive)
            RETURNING id
            """, request, transaction, cancellationToken: cancellationToken));
        await ReplaceRolesAsync(connection, transaction, menuId, request.AllRoleIds(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return menuId;
    }

    public async Task<bool> UpdateMenuAsync(int menuId, MenuUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE public."Menu"
            SET title = @Title,
                path = @Path,
                icon = @Icon,
                "sortOrder" = @SortOrder,
                "parentId" = @ParentId,
                "roleId" = NULL,
                "isActive" = @IsActive
            WHERE id = @MenuId
            """, new
            {
                MenuId = menuId,
                request.Title,
                request.Path,
                request.Icon,
                request.SortOrder,
                request.ParentId,
                request.IsActive
            }, transaction, cancellationToken: cancellationToken));
        if (updated == 0)
            return false;

        await ReplaceRolesAsync(connection, transaction, menuId, request.AllRoleIds(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static Task ReplaceRolesAsync(
        System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction transaction, int menuId, int[] roleIds,
        CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM public."MenuRole" WHERE "menuId" = @MenuId;
            INSERT INTO public."MenuRole" ("menuId", "roleId")
            SELECT @MenuId, role_id FROM unnest(@RoleIds) AS role_id;
            """, new { MenuId = menuId, RoleIds = roleIds }, transaction, cancellationToken: cancellationToken));

    public async Task<DeleteOutcome> DeleteMenuAsync(int menuId, CancellationToken cancellationToken = default)
    {
        // Submenus cascade on delete in the database, so a menu that still has children is kept.
        await using var connection = connectionFactory.CreateConnection();
        var deleted = await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM public."Menu"
            WHERE id = @MenuId
              AND NOT EXISTS (SELECT 1 FROM public."Menu" child WHERE child."parentId" = @MenuId)
            """, new { MenuId = menuId }, cancellationToken: cancellationToken));
        if (deleted > 0)
            return DeleteOutcome.Deleted;

        var exists = await connection.QuerySingleAsync<bool>(new CommandDefinition(
            """SELECT EXISTS (SELECT 1 FROM public."Menu" WHERE id = @MenuId)""",
            new { MenuId = menuId }, cancellationToken: cancellationToken));
        return exists ? DeleteOutcome.InUse : DeleteOutcome.NotFound;
    }

    public async Task<bool> IsSameOrDescendantAsync(int menuId, int rootMenuId, CancellationToken cancellationToken = default)
    {
        // Walk up from menuId; UNION (not UNION ALL) stops even if the data already has a cycle.
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            WITH RECURSIVE ancestors AS (
                SELECT id, "parentId" FROM public."Menu" WHERE id = @MenuId
                UNION
                SELECT m.id, m."parentId"
                FROM public."Menu" m
                JOIN ancestors a ON m.id = a."parentId")
            SELECT EXISTS (SELECT 1 FROM ancestors WHERE id = @RootMenuId)
            """, new { MenuId = menuId, RootMenuId = rootMenuId }, cancellationToken: cancellationToken));
    }
}
