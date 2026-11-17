using ThinkTechErpDomain.Models.ThinkCart;

namespace ThinkTechErpRepository.ThinkCart;

/// <summary>Checkout reference data (payment methods, zones, rates, locations), coupons and placing orders.</summary>
public interface IThinkCartCheckoutRepository
{
    Task<IReadOnlyList<PaymentMethodOption>> GetPaymentMethodsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShippingZoneOptionView>> GetShippingZonesAsync(CancellationToken cancellationToken = default);

    /// <summary>Active districts of active divisions.</summary>
    Task<IReadOnlyList<LocationDistrict>> GetDistrictsAsync(CancellationToken cancellationToken = default);

    /// <summary>Active areas of an active district; null when the district isn't one.</summary>
    Task<IReadOnlyList<LocationArea>?> GetAreasAsync(short districtId, CancellationToken cancellationToken = default);

    /// <summary>
    /// An active district (of an active division) and, when asked, its active area with their zones; null when the
    /// district isn't one. AreaId is null when the area doesn't belong to the district or isn't active.
    /// </summary>
    Task<DeliveryLocation?> GetDeliveryLocationAsync(
        short districtId, int? areaId, CancellationToken cancellationToken = default);

    /// <summary>An active zone (by id, else by code) and its active weight bands.</summary>
    Task<ShippingResolution?> GetShippingZoneAsync(
        short? zoneId, string? code, CancellationToken cancellationToken = default);

    /// <summary>
    /// An active coupon by code (case-insensitive) with its usage, the customer's usage and order count (when the
    /// customer is known) and which of these listings it applies to.
    /// </summary>
    Task<CouponInfo?> GetCouponAsync(
        int organizationId, string code, int? customerId, IReadOnlyList<int> listingIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Places an order in one transaction: locks the cart, re-reads and re-prices its lines, finds or creates
    /// the customer, re-reads the coupon (locked), lets <paramref name="price"/> check and price everything
    /// (it throws to reject), saves a new address, writes the order, its lines and status history, and marks
    /// the cart converted.
    /// </summary>
    Task<PlaceOrderOutcome> PlaceOrderAsync(
        PlaceOrderCommand command, Func<OrderPricingInput, OrderPricingResult> price,
        CancellationToken cancellationToken = default);
}

public enum PlaceOrderStatus
{
    Placed,
    CartNotFound,
    CartConverted,
    CustomerNotFound
}

public sealed record PlaceOrderOutcome(PlaceOrderStatus Status, OrderPlaced? Order = null);
