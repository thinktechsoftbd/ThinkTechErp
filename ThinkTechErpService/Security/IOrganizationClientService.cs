using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpService.Security;

public interface IOrganizationClientService
{
    Task<PagedResult<OrganizationClient>> GetClientsAsync(
        string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<PagedResult<OrganizationClientOrganizationOption>> GetOrganizationOptionsAsync(
        string? search, int limit, int offset, CancellationToken cancellationToken = default);
    Task<OrganizationClient?> GetClientAsync(int clientId, CancellationToken cancellationToken = default);
    Task<OrganizationClient> CreateClientAsync(
        OrganizationClientUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<OrganizationClient?> UpdateClientAsync(
        int clientId, OrganizationClientUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<OrganizationClient?> SetClientActiveAsync(
        int clientId, bool isActive, int userId, CancellationToken cancellationToken = default);
}
