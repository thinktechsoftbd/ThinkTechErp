using Dapper;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Database;

namespace ThinkTechErpRepository.Security;

public interface IUserOrganizationRepository
{
    /// <summary>The user's linked organizations, plus every active one when includeAll (Super Admin).</summary>
    Task<IReadOnlyList<UserOrganization>> GetAccessibleAsync(int userId, bool includeAll, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserOrganization>> GetLinkedAsync(int userId, CancellationToken cancellationToken = default);

    Task<bool> UserExistsAsync(int userId, CancellationToken cancellationToken = default);

    Task ReplaceLinksAsync(int userId, IReadOnlyCollection<int> organizationIds, int? defaultOrganizationId, int actingUserId,
        CancellationToken cancellationToken = default);
}

public sealed class UserOrganizationRepository(IDbConnectionFactory connectionFactory) : IUserOrganizationRepository
{
    public async Task<IReadOnlyList<UserOrganization>> GetAccessibleAsync(
        int userId, bool includeAll, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<UserOrganization>(new CommandDefinition("""
            SELECT o.organization_id AS OrganizationId,
                   o.organization_name_en AS Name,
                   o.organization_code AS Code,
                   coalesce(uo.is_default, false) AS IsDefault,
                   uo.user_id IS NOT NULL AS IsLinked,
                   o.is_external AS IsExternal
            FROM security.organization o
            LEFT JOIN security.user_organization uo ON uo.organization_id = o.organization_id AND uo.user_id = @UserId
            WHERE o.is_active AND (uo.user_id IS NOT NULL OR @IncludeAll)
            ORDER BY coalesce(uo.is_default, false) DESC, o.organization_name_en, o.organization_id
            """, new { UserId = userId, IncludeAll = includeAll }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<UserOrganization>> GetLinkedAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<UserOrganization>(new CommandDefinition("""
            SELECT o.organization_id AS OrganizationId, o.organization_name_en AS Name, o.organization_code AS Code,
                   uo.is_default AS IsDefault, true AS IsLinked, o.is_external AS IsExternal
            FROM security.user_organization uo
            JOIN security.organization o ON o.organization_id = uo.organization_id
            WHERE uo.user_id = @UserId
            ORDER BY uo.is_default DESC, o.organization_name_en
            """, new { UserId = userId }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<bool> UserExistsAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<bool>(new CommandDefinition(
            """SELECT EXISTS (SELECT 1 FROM security."User" WHERE id = @UserId)""", new { UserId = userId },
            cancellationToken: cancellationToken));
    }

    public async Task ReplaceLinksAsync(
        int userId, IReadOnlyCollection<int> organizationIds, int? defaultOrganizationId, int actingUserId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM security.user_organization WHERE user_id = @UserId AND NOT (organization_id = ANY(@Ids))",
            new { UserId = userId, Ids = organizationIds.ToArray() }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition($"""
            INSERT INTO security.user_organization (user_id, organization_id, is_default, created_at, created_by)
            SELECT @UserId, id, false, {SecurityDb.NowUtc}, @ActingUserId FROM unnest(@Ids) AS id
            ON CONFLICT ON CONSTRAINT pk_user_organization DO NOTHING
            """, new { UserId = userId, Ids = organizationIds.ToArray(), ActingUserId = actingUserId }, transaction,
            cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE security.user_organization SET is_default = false WHERE user_id = @UserId AND is_default;
            UPDATE security.user_organization SET is_default = true WHERE user_id = @UserId AND organization_id = @DefaultId;
            """, new { UserId = userId, DefaultId = defaultOrganizationId ?? organizationIds.FirstOrDefault() }, transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }
}
