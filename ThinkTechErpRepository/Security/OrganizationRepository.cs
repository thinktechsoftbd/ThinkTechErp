using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Sms;

namespace ThinkTechErpRepository.Security;

public sealed class OrganizationRepository(IDbConnectionFactory connectionFactory) : IOrganizationRepository
{
    private const string OrganizationSelect = """
        SELECT organization_id AS Id,
               object_id AS ObjectId,
               organization_name_en AS NameEn,
               organization_name_bn AS NameBn,
               slogan AS Slogan,
               organization_code AS Code,
               organization_slug AS Slug,
               email AS Email,
               phone AS Phone,
               website AS Website,
               address_line1 AS AddressLine1,
               address_line2 AS AddressLine2,
               city AS City,
               state AS State,
               postal_code AS PostalCode,
               country AS Country,
               logo_url AS LogoUrl,
               is_external AS IsExternal,
               is_active AS IsActive,
               sms_usage AS SmsUsage,
               is_finalized AS IsFinalized,
               sms_provider_id AS SmsProviderId,
               (SELECT p.provider_name FROM public.sms_provider p
                WHERE p.sms_provider_id = organization.sms_provider_id) AS SmsProviderName,
               created_at AT TIME ZONE 'utc' AS CreatedAt,
               created_by AS CreatedBy,
               updated_at AT TIME ZONE 'utc' AS UpdatedAt,
               updated_by AS UpdatedBy
        FROM public.organization
        """;

    public async Task<IReadOnlyList<OrganizationSmsProviderOption>> GetSmsProviderOptionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<OrganizationSmsProviderOption>(new CommandDefinition("""
            SELECT sms_provider_id AS Id, provider_name AS Name, provider_code AS Code, is_active AS IsActive,
                   (sms_sender_id IS NOT NULL AND btrim(sms_sender_id) <> '') AS HasSenderId
            FROM public.sms_provider
            ORDER BY is_active DESC, provider_name
            """, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<Organization>> GetOrganizationsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<Organization>(new CommandDefinition(
            $"{OrganizationSelect} ORDER BY organization_name_en, organization_id", cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<Organization?> GetOrganizationAsync(int organizationId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Organization>(new CommandDefinition(
            $"{OrganizationSelect} WHERE organization_id = @OrganizationId", new { OrganizationId = organizationId },
            cancellationToken: cancellationToken));
    }

    public async Task<Organization?> GetOrganizationByObjectIdAsync(Guid objectId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Organization>(new CommandDefinition(
            $"{OrganizationSelect} WHERE object_id = @ObjectId", new { ObjectId = objectId },
            cancellationToken: cancellationToken));
    }

    public async Task<int> CreateOrganizationAsync(
        OrganizationUpsertRequest request, int createdBy, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters(request);
        parameters.Add("UserId", createdBy);

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        // A new organization has no SMS allocated yet: its provider only has to exist and be active.
        if (request.SmsProviderId is int providerId)
        {
            var provider = (await SmsRepository.LockProvidersAsync(connection, transaction, [providerId], cancellationToken))[providerId];
            if (!provider.IsActive)
                throw new BusinessRuleException($"The SMS provider {provider.Name} is inactive. Activate it or choose another.");
        }

        var id = await connection.QuerySingleAsync<int>(new CommandDefinition($"""
            INSERT INTO public.organization (
                object_id, organization_name_en, organization_name_bn, slogan, organization_code, organization_slug, email,
                phone, website, address_line1, address_line2, city, state, postal_code, country, logo_url, is_external,
                is_active, is_finalized, sms_provider_id, created_at, created_by)
            VALUES (
                COALESCE(@ObjectId, gen_random_uuid()), @NameEn, @NameBn, @Slogan, @Code, @Slug, @Email,
                @Phone, @Website, @AddressLine1, @AddressLine2, @City, @State, @PostalCode, @Country, @LogoUrl, @IsExternal,
                @IsActive, @IsFinalized, @SmsProviderId, {SecurityDb.NowUtc}, @UserId)
            RETURNING organization_id
            """, parameters, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<bool> UpdateOrganizationAsync(
        int organizationId, OrganizationUpsertRequest request, int updatedBy, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters(request);
        parameters.Add("OrganizationId", organizationId);
        parameters.Add("UserId", updatedBy);

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var (found, currentProviderId) = await connection.QuerySingleOrDefaultAsync<(bool, int?)>(new CommandDefinition(
            "SELECT true, sms_provider_id FROM public.organization WHERE organization_id = @OrganizationId",
            new { OrganizationId = organizationId }, transaction, cancellationToken: cancellationToken));
        if (!found)
            return false;
        if (currentProviderId != request.SmsProviderId)
            await CheckSmsProviderChangeAsync(connection, transaction, organizationId, currentProviderId, request.SmsProviderId, cancellationToken);

        var updated = await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.organization
            SET object_id = COALESCE(@ObjectId, object_id),
                organization_name_en = @NameEn,
                organization_name_bn = @NameBn,
                slogan = @Slogan,
                organization_code = @Code,
                organization_slug = @Slug,
                email = @Email,
                phone = @Phone,
                website = @Website,
                address_line1 = @AddressLine1,
                address_line2 = @AddressLine2,
                city = @City,
                state = @State,
                postal_code = @PostalCode,
                country = @Country,
                logo_url = @LogoUrl,
                is_external = @IsExternal,
                is_active = @IsActive,
                is_finalized = @IsFinalized,
                sms_provider_id = @SmsProviderId,
                updated_at = {SecurityDb.NowUtc},
                updated_by = @UserId
            WHERE organization_id = @OrganizationId
            """, parameters, transaction, cancellationToken: cancellationToken)) > 0;
        await transaction.CommitAsync(cancellationToken);
        return updated;
    }

    /// <summary>
    /// An organization's SMS allocations come from its provider's stock, so a new provider must be active and have
    /// them left to allocate, and an organization with SMS allocated can't be left without one. Locks the providers and
    /// then the organization, in the order SMS allocations do (SmsRepository), so they can't deadlock.
    /// </summary>
    private static async Task CheckSmsProviderChangeAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, int? currentProviderId,
        int? newProviderId, CancellationToken cancellationToken)
    {
        int[] providerIds = [.. new[] { currentProviderId, newProviderId }.OfType<int>()];
        var providers = await SmsRepository.LockProvidersAsync(connection, transaction, providerIds, cancellationToken);
        var organization = await connection.QuerySingleAsync<(string Name, int? ProviderId, long Allocated)>(new CommandDefinition("""
            SELECT o.organization_name_en, o.sms_provider_id,
                   coalesce((SELECT sum(a.allocated_sms) FROM public.sms_allocation a WHERE a.organization_id = o.object_id), 0)::bigint
            FROM public.organization o
            WHERE o.organization_id = @OrganizationId
            FOR UPDATE OF o
            """, new { OrganizationId = organizationId }, transaction, cancellationToken: cancellationToken));
        if (organization.ProviderId != currentProviderId)
            throw new BusinessConflictException($"{organization.Name}'s SMS provider was just changed. Reload and try again.");

        if (newProviderId is not int providerId)
        {
            if (organization.Allocated > 0)
                throw new BusinessRuleException(
                    $"{organization.Name} has {organization.Allocated:N0} SMS allocated from {providers[currentProviderId!.Value].Name}, so it needs an SMS provider.");
            return;
        }

        var provider = providers[providerId];
        if (!provider.IsActive)
            throw new BusinessRuleException($"The SMS provider {provider.Name} is inactive. Activate it or choose another.");
        var available = provider.Purchased - provider.Allocated;
        if (organization.Allocated > available)
            throw new BusinessRuleException(
                $"{organization.Name}'s {organization.Allocated:N0} SMS allocated would move to {provider.Name}, which has only {available:N0} left to allocate. Buy more from {provider.Name} first.");
    }
}
