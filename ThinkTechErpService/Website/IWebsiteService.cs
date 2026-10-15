using ThinkTechErpDomain.Models;

namespace ThinkTechErpService.Website;

public interface IWebsiteService
{
    Task<IReadOnlyList<WebsiteRecord>> GetAllAsync(string table, CancellationToken cancellationToken = default);
    Task<WebsiteRecord?> GetByIdAsync(string table, string id, CancellationToken cancellationToken = default);
    Task<WebsiteRecord> CreateAsync(string table, WebsiteRecord record, CancellationToken cancellationToken = default);
    Task<WebsiteRecord?> UpdateAsync(string table, string id, WebsiteRecord record, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string table, string id, CancellationToken cancellationToken = default);
}
