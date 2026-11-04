using Dapper;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Database;

namespace ThinkTechErpRepository.Security;

public sealed class OrganizationClientRepository(IDbConnectionFactory connectionFactory) : IOrganizationClientRepository
{
    private const string ClientSelect = """
        SELECT c.id AS Id,
               c.object_id AS ObjectId,
               c.client_name AS ClientName,
               c.org_object_id AS OrgObjectId,
               o.organization_name_en AS OrganizationName,
               c.acting_user_id AS ActingUserId,
               u.name AS ActingUserName,
               c.scopes AS Scopes,
               c.is_active AS IsActive,
               (c.secret_sha256_hash IS NOT NULL) AS HasSecretSha256Hash,
               c.created_at AT TIME ZONE 'utc' AS CreatedAt,
               c.created_by AS CreatedBy,
               c.updated_at AT TIME ZONE 'utc' AS UpdatedAt,
               c.updated_by AS UpdatedBy
        FROM public.security_organization_client c
        JOIN public."User" u ON u.id = c.acting_user_id
        LEFT JOIN public.organization o ON o.object_id = c.org_object_id
        """;

    private const string ClientFilter = """
        WHERE (CAST(@Pattern AS text) IS NULL OR c.client_name ILIKE @Pattern
               OR EXISTS (SELECT 1 FROM unnest(c.scopes) AS s(value) WHERE s.value ILIKE @Pattern))
          AND (CAST(@IsActive AS boolean) IS NULL OR c.is_active = @IsActive)
        """;

    public async Task<PagedResult<OrganizationClient>> GetClientsAsync(
        string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default)
    {
        var parameters = new
        {
            Pattern = search is null ? null : SecurityDb.ContainsPattern(search),
            IsActive = isActive,
            Limit = limit,
            Offset = offset
        };

        await using var connection = connectionFactory.CreateConnection();
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT count(*) FROM public.security_organization_client c {ClientFilter};
            {ClientSelect} {ClientFilter}
            ORDER BY c.client_name, c.id
            LIMIT @Limit OFFSET @Offset;
            """, parameters, cancellationToken: cancellationToken));
        var totalCount = await results.ReadSingleAsync<long>();
        var items = (await results.ReadAsync<OrganizationClient>()).AsList();
        return new PagedResult<OrganizationClient>
        {
            Items = items,
            TotalCount = (int)totalCount,
            Limit = limit,
            Offset = offset
        };
    }

    public async Task<PagedResult<OrganizationClientOrganizationOption>> GetOrganizationOptionsAsync(
        string? search, int limit, int offset, CancellationToken cancellationToken = default)
    {
        var pattern = search is null ? null : SecurityDb.ContainsPattern(search);
        await using var connection = connectionFactory.CreateConnection();
        using var results = await connection.QueryMultipleAsync(new CommandDefinition("""
            SELECT count(*) FROM public.organization o
            WHERE CAST(@Pattern AS text) IS NULL OR o.organization_name_en ILIKE @Pattern
               OR o.organization_code ILIKE @Pattern OR o.object_id::text ILIKE @Pattern;
            SELECT o.object_id AS ObjectId, o.organization_name_en AS NameEn,
                   o.organization_code AS Code, o.is_active AS IsActive
            FROM public.organization o
            WHERE CAST(@Pattern AS text) IS NULL OR o.organization_name_en ILIKE @Pattern
               OR o.organization_code ILIKE @Pattern OR o.object_id::text ILIKE @Pattern
            ORDER BY o.organization_name_en, o.organization_id
            LIMIT @Limit OFFSET @Offset;
            """, new { Pattern = pattern, Limit = limit, Offset = offset }, cancellationToken: cancellationToken));
        var totalCount = await results.ReadSingleAsync<long>();
        var items = (await results.ReadAsync<OrganizationClientOrganizationOption>()).AsList();
        return new PagedResult<OrganizationClientOrganizationOption>
        {
            Items = items,
            TotalCount = (int)totalCount,
            Limit = limit,
            Offset = offset
        };
    }

    public async Task<OrganizationClient?> GetClientAsync(int clientId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<OrganizationClient>(new CommandDefinition(
            $"{ClientSelect} WHERE c.id = @ClientId",
            new { ClientId = clientId }, cancellationToken: cancellationToken));
    }

    public async Task<OrganizationClientCredentials?> GetClientCredentialsAsync(
        string clientName, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<OrganizationClientCredentials>(new CommandDefinition("""
            SELECT client_name AS ClientName,
                   secret_sha256_hash AS SecretSha256Hash,
                   acting_user_id AS ActingUserId,
                   scopes AS Scopes,
                   is_active AS IsActive
            FROM public.security_organization_client
            WHERE client_name = @ClientName
            """, new { ClientName = clientName }, cancellationToken: cancellationToken));
    }

    public async Task<bool> ClientNameExistsAsync(
        string clientName, int? excludeClientId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1 FROM public.security_organization_client
                WHERE lower(client_name) = lower(@ClientName)
                  AND (CAST(@ExcludeId AS integer) IS NULL OR id <> @ExcludeId))
            """, new { ClientName = clientName, ExcludeId = excludeClientId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> HasActingUserScopeConflictAsync(
        int actingUserId, int? excludeClientId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1 FROM public.security_organization_client
                WHERE acting_user_id = @ActingUserId
                  AND id <> COALESCE(@ExcludeClientId, 0)
                  AND scopes && ARRAY['organization.sync', 'sms.usage']::text[])
            """, new { ActingUserId = actingUserId, ExcludeClientId = excludeClientId },
            cancellationToken: cancellationToken));
    }

    public async Task<int> CreateClientAsync(
        OrganizationClientUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<int>(new CommandDefinition($"""
            INSERT INTO public.security_organization_client
                (client_name, org_object_id, secret_sha256_hash, acting_user_id, scopes, is_active, created_at, created_by)
            VALUES (@ClientName, @OrgObjectId, @SecretSha256Hash, @ActingUserId, @Scopes, @IsActive, {SecurityDb.NowUtc}, @UserId)
            RETURNING id
            """, new
            {
                request.ClientName,
                request.OrgObjectId,
                request.SecretSha256Hash,
                request.ActingUserId,
                request.Scopes,
                request.IsActive,
                UserId = userId
            }, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateClientAsync(
        int clientId, OrganizationClientUpsertRequest request, int userId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.security_organization_client
            SET client_name = @ClientName,
                org_object_id = @OrgObjectId,
                secret_sha256_hash = COALESCE(@SecretSha256Hash, secret_sha256_hash),
                acting_user_id = @ActingUserId,
                scopes = @Scopes,
                is_active = @IsActive,
                updated_at = {SecurityDb.NowUtc},
                updated_by = @UserId
            WHERE id = @ClientId
            """, new
            {
                ClientId = clientId,
                request.ClientName,
                request.OrgObjectId,
                request.SecretSha256Hash,
                request.ActingUserId,
                request.Scopes,
                request.IsActive,
                UserId = userId
            }, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<bool> SetClientActiveAsync(
        int clientId, bool isActive, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.security_organization_client
            SET is_active = @IsActive,
                updated_at = {SecurityDb.NowUtc},
                updated_by = @UserId
            WHERE id = @ClientId
            """, new { ClientId = clientId, IsActive = isActive, UserId = userId },
            cancellationToken: cancellationToken)) > 0;
    }
}
