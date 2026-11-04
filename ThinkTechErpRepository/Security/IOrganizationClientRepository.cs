using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpRepository.Security;

public interface IOrganizationClientRepository
{
    Task<PagedResult<OrganizationClient>> GetClientsAsync(
        string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<PagedResult<OrganizationClientOrganizationOption>> GetOrganizationOptionsAsync(
        string? search, int limit, int offset, CancellationToken cancellationToken = default);
    Task<OrganizationClient?> GetClientAsync(int clientId, CancellationToken cancellationToken = default);
    Task<OrganizationClientCredentials?> GetClientCredentialsAsync(string clientName, CancellationToken cancellationToken = default);
    Task<bool> ClientNameExistsAsync(
        string clientName, int? excludeClientId, CancellationToken cancellationToken = default);
    Task<bool> HasActingUserScopeConflictAsync(
        int actingUserId, int? excludeClientId, CancellationToken cancellationToken = default);
    Task<int> CreateClientAsync(
        OrganizationClientUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<bool> UpdateClientAsync(
        int clientId, OrganizationClientUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<bool> SetClientActiveAsync(
        int clientId, bool isActive, int userId, CancellationToken cancellationToken = default);
}
