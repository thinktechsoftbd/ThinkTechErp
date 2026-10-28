using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpRepository.Settings;
using ThinkTechErpService.Security;

namespace ThinkTechErpService.Settings;

public sealed class LookupService(ILookupRepository lookups) : ILookupService
{
    public Task<PagedResult<LookupTable>> GetTablesAsync(
        string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default)
        => lookups.GetTablesAsync(Clean(search), isActive, limit, offset, cancellationToken);

    public Task<LookupTable?> GetTableAsync(long lookupTableId, CancellationToken cancellationToken = default)
        => lookups.GetTableAsync(lookupTableId, cancellationToken);

    public async Task<LookupTable> CreateTableAsync(
        LookupTableUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        request.Name = request.Name.Trim();
        if (await lookups.TableNameExistsAsync(request.Name, null, cancellationToken))
            throw new SecurityConflictException("A lookup table with this name already exists.");

        var lookupTableId = await SecurityDbErrors.ExecuteAsync(() =>
            lookups.CreateTableAsync(request, userId, cancellationToken));
        return await lookups.GetTableAsync(lookupTableId, cancellationToken)
            ?? throw new InvalidOperationException("Created lookup table could not be read back.");
    }

    public async Task<LookupTable?> UpdateTableAsync(
        long lookupTableId, LookupTableUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        request.Name = request.Name.Trim();
        if (await lookups.TableNameExistsAsync(request.Name, lookupTableId, cancellationToken))
            throw new SecurityConflictException("A lookup table with this name already exists.");

        if (!await SecurityDbErrors.ExecuteAsync(() =>
                lookups.UpdateTableAsync(lookupTableId, request, userId, cancellationToken)))
            return null;

        return await lookups.GetTableAsync(lookupTableId, cancellationToken);
    }

    public Task<DeleteOutcome> DeleteTableAsync(long lookupTableId, CancellationToken cancellationToken = default)
        => lookups.DeleteTableAsync(lookupTableId, cancellationToken);

    public Task<PagedResult<LookupData>> GetDataAsync(
        long? lookupTableId, string? search, bool? isActive, int limit, int offset,
        CancellationToken cancellationToken = default)
        => lookups.GetDataAsync(lookupTableId, Clean(search), isActive, limit, offset, cancellationToken);

    public Task<LookupData?> GetDataItemAsync(long lookupDataId, CancellationToken cancellationToken = default)
        => lookups.GetDataItemAsync(lookupDataId, cancellationToken);

    public async Task<LookupData> CreateDataAsync(
        LookupDataUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        request.Name = request.Name.Trim();
        if (await lookups.DataNameExistsAsync(request.LookupTableId, request.Name, null, cancellationToken))
            throw new SecurityConflictException("This lookup table already has a value with this name.");

        var lookupDataId = await SecurityDbErrors.ExecuteAsync(() =>
            lookups.CreateDataAsync(request, userId, cancellationToken));
        return await lookups.GetDataItemAsync(lookupDataId, cancellationToken)
            ?? throw new InvalidOperationException("Created lookup value could not be read back.");
    }

    public async Task<LookupData?> UpdateDataAsync(
        long lookupDataId, LookupDataUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        request.Name = request.Name.Trim();
        if (await lookups.DataNameExistsAsync(request.LookupTableId, request.Name, lookupDataId, cancellationToken))
            throw new SecurityConflictException("This lookup table already has a value with this name.");

        if (!await SecurityDbErrors.ExecuteAsync(() =>
                lookups.UpdateDataAsync(lookupDataId, request, userId, cancellationToken)))
            return null;

        return await lookups.GetDataItemAsync(lookupDataId, cancellationToken);
    }

    public Task<DeleteOutcome> DeleteDataAsync(long lookupDataId, CancellationToken cancellationToken = default)
        => lookups.DeleteDataAsync(lookupDataId, cancellationToken);

    public Task<IReadOnlyList<LookupOption>> GetOptionsAsync(
        string lookupTableName, CancellationToken cancellationToken = default)
        => lookups.GetOptionsAsync(lookupTableName.Trim(), cancellationToken);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
