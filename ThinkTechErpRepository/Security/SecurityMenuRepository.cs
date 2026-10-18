using Dapper;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Database;

namespace ThinkTechErpRepository.Security;

public sealed class SecurityMenuRepository(IDbConnectionFactory connectionFactory) : ISecurityMenuRepository
{
    private const string MenuSelect = """
        SELECT id AS Id,
               title AS Title,
               path AS Path,
               icon AS Icon,
               "sortOrder" AS SortOrder,
               "parentId" AS ParentId,
               "roleId" AS RoleId,
               "isActive" AS IsActive
        FROM security."Menu"
        """;

    public async Task<IReadOnlyList<SecurityMenu>> GetMenusAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        var rows = await connection.QueryAsync<SecurityMenu>(new CommandDefinition(
            $"""{MenuSelect} ORDER BY "parentId" NULLS FIRST, "sortOrder", id""",
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<SecurityMenu?> GetMenuAsync(int menuId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleOrDefaultAsync<SecurityMenu>(new CommandDefinition(
            $"{MenuSelect} WHERE id = @MenuId", new { MenuId = menuId }, cancellationToken: cancellationToken));
    }

    public async Task<int> CreateMenuAsync(MenuUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleAsync<int>(new CommandDefinition("""
            INSERT INTO security."Menu" (title, path, icon, "sortOrder", "parentId", "roleId", "isActive")
            VALUES (@Title, @Path, @Icon, @SortOrder, @ParentId, @RoleId, @IsActive)
            RETURNING id
            """, request, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateMenuAsync(int menuId, MenuUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE security."Menu"
            SET title = @Title,
                path = @Path,
                icon = @Icon,
                "sortOrder" = @SortOrder,
                "parentId" = @ParentId,
                "roleId" = @RoleId,
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
                request.RoleId,
                request.IsActive
            }, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<DeleteOutcome> DeleteMenuAsync(int menuId, CancellationToken cancellationToken = default)
    {
        // Submenus cascade on delete in the database, so a menu that still has children is kept.
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        var deleted = await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM security."Menu"
            WHERE id = @MenuId
              AND NOT EXISTS (SELECT 1 FROM security."Menu" child WHERE child."parentId" = @MenuId)
            """, new { MenuId = menuId }, cancellationToken: cancellationToken));
        if (deleted > 0)
            return DeleteOutcome.Deleted;

        var exists = await connection.QuerySingleAsync<bool>(new CommandDefinition(
            """SELECT EXISTS (SELECT 1 FROM security."Menu" WHERE id = @MenuId)""",
            new { MenuId = menuId }, cancellationToken: cancellationToken));
        return exists ? DeleteOutcome.InUse : DeleteOutcome.NotFound;
    }

    public async Task<bool> IsSameOrDescendantAsync(int menuId, int rootMenuId, CancellationToken cancellationToken = default)
    {
        // Walk up from menuId; UNION (not UNION ALL) stops even if the data already has a cycle.
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            WITH RECURSIVE ancestors AS (
                SELECT id, "parentId" FROM security."Menu" WHERE id = @MenuId
                UNION
                SELECT m.id, m."parentId"
                FROM security."Menu" m
                JOIN ancestors a ON m.id = a."parentId")
            SELECT EXISTS (SELECT 1 FROM ancestors WHERE id = @RootMenuId)
            """, new { MenuId = menuId, RootMenuId = rootMenuId }, cancellationToken: cancellationToken));
    }
}
