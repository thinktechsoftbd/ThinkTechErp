using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;

namespace ThinkTechErpService.Settings;

public interface ILookupService
{
    Task<PagedResult<LookupTable>> GetTablesAsync(string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<LookupTable?> GetTableAsync(long lookupTableId, CancellationToken cancellationToken = default);
    Task<LookupTable> CreateTableAsync(LookupTableUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<LookupTable?> UpdateTableAsync(long lookupTableId, LookupTableUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteTableAsync(long lookupTableId, CancellationToken cancellationToken = default);

    Task<PagedResult<LookupData>> GetDataAsync(long? lookupTableId, string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<LookupData?> GetDataItemAsync(long lookupDataId, CancellationToken cancellationToken = default);
    Task<LookupData> CreateDataAsync(LookupDataUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<LookupData?> UpdateDataAsync(long lookupDataId, LookupDataUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteDataAsync(long lookupDataId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LookupOption>> GetOptionsAsync(string lookupTableName, CancellationToken cancellationToken = default);
}
