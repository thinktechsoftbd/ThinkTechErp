using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;

namespace ThinkTechErpRepository.Settings;

public interface IDistrictRepository
{
    Task<PagedResult<District>> GetDistrictsAsync(
        short? divisionId, short? shippingZoneId, string? search, bool? isActive, int limit, int offset,
        CancellationToken cancellationToken = default);
    Task<District?> GetDistrictAsync(short districtId, CancellationToken cancellationToken = default);
    Task<bool> DistrictNameExistsAsync(
        short divisionId, string name, short? excludeDistrictId, CancellationToken cancellationToken = default);
    Task<short> CreateDistrictAsync(DistrictUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<bool> UpdateDistrictAsync(
        short districtId, DistrictUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteDistrictAsync(short districtId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DivisionOption>> GetDivisionOptionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShippingZoneOption>> GetShippingZoneOptionsAsync(CancellationToken cancellationToken = default);
}
