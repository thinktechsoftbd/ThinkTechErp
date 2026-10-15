using ThinkTechErpDomain.Models;

namespace ThinkTechErpRepository.Website;

public interface IWebsiteRepository
{
    Task<IReadOnlyList<WebsiteRecord>> GetAllAsync(string table, CancellationToken cancellationToken = default);
    Task<WebsiteRecord?> GetByIdAsync(string table, string id, CancellationToken cancellationToken = default);
    Task<WebsiteRecord> CreateAsync(string table, WebsiteRecord record, CancellationToken cancellationToken = default);
    Task<WebsiteRecord?> UpdateAsync(string table, string id, WebsiteRecord record, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string table, string id, CancellationToken cancellationToken = default);
}
