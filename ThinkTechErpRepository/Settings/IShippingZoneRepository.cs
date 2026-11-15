using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;

namespace ThinkTechErpRepository.Settings;

public interface IShippingZoneRepository
{
    Task<PagedResult<ShippingZone>> GetShippingZonesAsync(
        string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<ShippingZone?> GetShippingZoneAsync(short shippingZoneId, CancellationToken cancellationToken = default);
    Task<bool> ShippingZoneCodeExistsAsync(string code, short? excludeShippingZoneId, CancellationToken cancellationToken = default);
    Task<short> CreateShippingZoneAsync(ShippingZoneUpsertRequest request, CancellationToken cancellationToken = default);
    Task<bool> UpdateShippingZoneAsync(
        short shippingZoneId, ShippingZoneUpsertRequest request, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteShippingZoneAsync(short shippingZoneId, CancellationToken cancellationToken = default);
}
