using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpRepository.Settings;
using ThinkTechErpService.Security;

namespace ThinkTechErpService.Settings;

public sealed class AreaService(IAreaRepository areas) : IAreaService
{
    public Task<PagedResult<Area>> GetAreasAsync(
        short? districtId, string? areaType, string? search, bool? isActive, int limit, int offset,
        CancellationToken cancellationToken = default)
        => areas.GetAreasAsync(districtId, CleanAreaType(areaType), Clean(search), isActive, limit, offset, cancellationToken);

    public Task<Area?> GetAreaAsync(int areaId, CancellationToken cancellationToken = default)
        => areas.GetAreaAsync(areaId, cancellationToken);

    public async Task<Area> CreateAreaAsync(
        AreaUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        if (await areas.AreaNameExistsAsync(request.DistrictId, request.Name, null, cancellationToken))
            throw new SecurityConflictException("This district already has an area with this name.");

        var areaId = await SecurityDbErrors.ExecuteAsync(() =>
            areas.CreateAreaAsync(request, userId, cancellationToken));
        return await areas.GetAreaAsync(areaId, cancellationToken)
            ?? throw new InvalidOperationException("Created area could not be read back.");
    }

    public async Task<Area?> UpdateAreaAsync(
        int areaId, AreaUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        if (await areas.AreaNameExistsAsync(request.DistrictId, request.Name, areaId, cancellationToken))
            throw new SecurityConflictException("This district already has an area with this name.");

        if (!await SecurityDbErrors.ExecuteAsync(() =>
                areas.UpdateAreaAsync(areaId, request, userId, cancellationToken)))
            return null;

        return await areas.GetAreaAsync(areaId, cancellationToken);
    }

    public Task<DeleteOutcome> DeleteAreaAsync(int areaId, CancellationToken cancellationToken = default)
        => areas.DeleteAreaAsync(areaId, cancellationToken);

    public Task<PagedResult<DistrictOption>> GetDistrictsAsync(
        string? search, int limit, int offset, CancellationToken cancellationToken = default)
        => areas.GetDistrictsAsync(Clean(search), limit, offset, cancellationToken);

    public Task<IReadOnlyList<ShippingZoneOption>> GetShippingZonesAsync(
        CancellationToken cancellationToken = default)
        => areas.GetShippingZonesAsync(cancellationToken);

    /// <summary>Trims the text and checks the values the database constrains.</summary>
    private static void Normalize(AreaUpsertRequest request)
    {
        request.Name = request.Name.Trim();
        request.NameBn = Clean(request.NameBn);
        request.AreaType = request.AreaType.Trim().ToLowerInvariant();

        if (!AreaRules.AreaTypes.Contains(request.AreaType))
            throw new SecurityValidationException(
                $"Area type must be one of: {string.Join(", ", AreaRules.AreaTypes)}.");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>An unknown area type filters nothing rather than returning an empty list.</summary>
    private static string? CleanAreaType(string? areaType)
    {
        var cleaned = Clean(areaType)?.ToLowerInvariant();
        return cleaned is not null && AreaRules.AreaTypes.Contains(cleaned) ? cleaned : null;
    }
}
