using Dapper;
using ThinkTechErpDomain.Models.Sms;
using ThinkTechErpRepository.Database;

namespace ThinkTechErpRepository.Sms;

/// <summary>Reads what calling a provider's SMS gateway needs, secrets included (kept apart from ISmsRepository's public models).</summary>
public interface ISmsGatewayRepository
{
    /// <summary>The organization (by Object Id) and the provider it sends through; null when there is no such organization.</summary>
    Task<SmsGatewayOrganization?> GetOrganizationAsync(Guid objectId, CancellationToken cancellationToken = default);

    Task<SmsGatewayProvider?> GetProviderAsync(int providerId, CancellationToken cancellationToken = default);
}

public sealed class SmsGatewayRepository(IDbConnectionFactory connectionFactory) : ISmsGatewayRepository
{
    public async Task<SmsGatewayOrganization?> GetOrganizationAsync(Guid objectId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<SmsGatewayOrganization>(new CommandDefinition("""
            SELECT object_id AS ObjectId, organization_name_en AS Name, sms_provider_id AS ProviderId
            FROM public.organization
            WHERE object_id = @ObjectId
            """, new { ObjectId = objectId }, cancellationToken: cancellationToken));
    }

    public async Task<SmsGatewayProvider?> GetProviderAsync(int providerId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<SmsGatewayProvider>(new CommandDefinition("""
            SELECT sms_provider_id AS ProviderId, provider_name AS Name, provider_code AS Code, api_base_url AS ApiBaseUrl,
                   api_username AS ApiUsername, api_password AS ApiPassword, api_key AS ApiKey, sms_sender_id AS SenderId,
                   is_active AS IsActive
            FROM public.sms_provider
            WHERE sms_provider_id = @ProviderId
            """, new { ProviderId = providerId }, cancellationToken: cancellationToken));
    }
}
