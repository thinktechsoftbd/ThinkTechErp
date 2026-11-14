using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;

namespace ThinkTechErpService.Settings;

public interface IAreaService
{
    Task<PagedResult<Area>> GetAreasAsync(
        short? districtId, string? areaType, string? search, bool? isActive, int limit, int offset,
        CancellationToken cancellationToken = default);
    Task<Area?> GetAreaAsync(int areaId, CancellationToken cancellationToken = default);
    Task<Area> CreateAreaAsync(AreaUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<Area?> UpdateAreaAsync(
        int areaId, AreaUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteAreaAsync(int areaId, CancellationToken cancellationToken = default);

    Task<PagedResult<DistrictOption>> GetDistrictsAsync(
        string? search, int limit, int offset, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShippingZoneOption>> GetShippingZonesAsync(CancellationToken cancellationToken = default);
}
