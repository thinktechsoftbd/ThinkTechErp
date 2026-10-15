using ThinkTechErpDomain.Models;
using ThinkTechErpRepository.Website;

namespace ThinkTechErpService.Website;

public sealed class WebsiteService(IWebsiteRepository repository) : IWebsiteService
{
    public Task<IReadOnlyList<WebsiteRecord>> GetAllAsync(string table, CancellationToken cancellationToken = default)
        => repository.GetAllAsync(table, cancellationToken);

    public Task<WebsiteRecord?> GetByIdAsync(string table, string id, CancellationToken cancellationToken = default)
        => repository.GetByIdAsync(table, id, cancellationToken);

    public Task<WebsiteRecord> CreateAsync(string table, WebsiteRecord record, CancellationToken cancellationToken = default)
        => repository.CreateAsync(table, record, cancellationToken);

    public Task<WebsiteRecord?> UpdateAsync(
        string table,
        string id,
        WebsiteRecord record,
        CancellationToken cancellationToken = default)
        => repository.UpdateAsync(table, id, record, cancellationToken);

    public Task<bool> DeleteAsync(string table, string id, CancellationToken cancellationToken = default)
        => repository.DeleteAsync(table, id, cancellationToken);
}
