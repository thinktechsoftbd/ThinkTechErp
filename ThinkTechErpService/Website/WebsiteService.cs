using Npgsql;
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
        => ExecuteWriteAsync(() => repository.CreateAsync(table, record, cancellationToken));

    public Task<WebsiteRecord?> UpdateAsync(
        string table,
        string id,
        WebsiteRecord record,
        CancellationToken cancellationToken = default)
        => ExecuteWriteAsync(() => repository.UpdateAsync(table, id, record, cancellationToken));

    public Task<bool> DeleteAsync(string table, string id, CancellationToken cancellationToken = default)
        => repository.DeleteAsync(table, id, cancellationToken);

    // Values the column cannot hold (bad dates, too-long text, missing required fields, duplicates)
    // are client errors; the controller turns ArgumentException into 400.
    private static async Task<T> ExecuteWriteAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (PostgresException exception) when (
            exception.SqlState.StartsWith("22", StringComparison.Ordinal) ||
            exception.SqlState.StartsWith("23", StringComparison.Ordinal) ||
            exception.SqlState is PostgresErrorCodes.DatatypeMismatch or PostgresErrorCodes.CannotCoerce)
        {
            var field = exception.ColumnName is null ? string.Empty : $" ({exception.ColumnName})";
            throw new ArgumentException($"Invalid record value{field}: {exception.MessageText}", exception);
        }
    }
}
