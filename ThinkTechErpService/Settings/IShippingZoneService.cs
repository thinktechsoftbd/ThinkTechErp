using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;

namespace ThinkTechErpService.Settings;

public interface IShippingZoneService
{
    Task<PagedResult<ShippingZone>> GetShippingZonesAsync(
        string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<ShippingZone?> GetShippingZoneAsync(short shippingZoneId, CancellationToken cancellationToken = default);
    Task<ShippingZone> CreateShippingZoneAsync(ShippingZoneUpsertRequest request, CancellationToken cancellationToken = default);
    Task<ShippingZone?> UpdateShippingZoneAsync(
        short shippingZoneId, ShippingZoneUpsertRequest request, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteShippingZoneAsync(short shippingZoneId, CancellationToken cancellationToken = default);
}
