using System.Text.RegularExpressions;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.ThinkCart;
using ThinkTechErpRepository.ThinkCart;

namespace ThinkTechErpService.ThinkCart;

public interface IThinkCartCheckoutService
{
    Task<CheckoutOptions> GetOptionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocationDistrict>> GetDistrictsAsync(CancellationToken cancellationToken = default);

    /// <summary>Null when the district isn't an active one.</summary>
    Task<IReadOnlyList<LocationArea>?> GetAreasAsync(short districtId, CancellationToken cancellationToken = default);

    /// <summary>Null when the cart doesn't exist or became an order.</summary>
    Task<CheckoutQuote?> QuoteAsync(
        int organizationId, int? customerId, CheckoutQuoteRequest request, CancellationToken cancellationToken = default);

    Task<PlaceOrderOutcome> PlaceOrderAsync(
        int organizationId, int? customerId, PlaceOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>Guest tracking: the order with this number shipped to this phone, or null.</summary>
    Task<OrderDetail?> TrackOrderAsync(
        int organizationId, string? orderNumber, string? phone, CancellationToken cancellationToken = default);
}

public sealed partial class ThinkCartCheckoutService(
    IThinkCartCheckoutRepository checkout,
    IThinkCartCartRepository carts,
    IThinkCartCatalogRepository catalog,
    IThinkCartOrderRepository orders) : IThinkCartCheckoutService
{
    private static readonly string[] CheckoutKeys = [ThinkCartSiteSettings.Checkout, ThinkCartSiteSettings.Shipping];

    public async Task<CheckoutOptions> GetOptionsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await catalog.GetSiteSettingsAsync([ThinkCartSiteSettings.Shipping], cancellationToken);
        return new CheckoutOptions
        {
            PaymentMethods = await checkout.GetPaymentMethodsAsync(cancellationToken),
            ShippingZones = await checkout.GetShippingZonesAsync(cancellationToken),
            DefaultZoneCode = ThinkCartSiteSettings.DefaultZoneCode(settings)
        };
    }

    public Task<IReadOnlyList<LocationDistrict>> GetDistrictsAsync(CancellationToken cancellationToken = default)
        => checkout.GetDistrictsAsync(cancellationToken);

    public Task<IReadOnlyList<LocationArea>?> GetAreasAsync(short districtId, CancellationToken cancellationToken = default)
        => checkout.GetAreasAsync(districtId, cancellationToken);

    public async Task<CheckoutQuote?> QuoteAsync(
        int organizationId, int? customerId, CheckoutQuoteRequest request, CancellationToken cancellationToken = default)
    {
        var cart = await carts.GetCartAsync(request.CartId, cancellationToken);
        if (cart is not { IsOpen: true })
            return null;

        var settings = await catalog.GetSiteSettingsAsync(CheckoutKeys, cancellationToken);
        var zone = await ResolveZoneAsync(request.DistrictId, request.AreaId, settings, cancellationToken);
        var lines = await carts.GetCartLinesAsync(organizationId, cart.Id, cancellationToken);
        var subtotal = ThinkCartPricingEngine.Subtotal(lines);
        var weight = ThinkCartPricingEngine.TotalWeight(lines);

        // A guest's per-customer and first-order limits are checked again when the order is placed.
        var code = CleanCode(request.CouponCode);
        var coupon = code is null
            ? null
            : await checkout.GetCouponAsync(
                organizationId, code, customerId, lines.Select(line => line.ListingId).Distinct().ToList(), cancellationToken);
        var outcome = ThinkCartPricingEngine.EvaluateCoupon(code, coupon, lines, subtotal, DateTime.UtcNow);
        var fee = outcome.FreeShipping ? 0 : ThinkCartPricingEngine.ShippingFee(zone, weight, subtotal);

        return new CheckoutQuote
        {
            Items = lines.Select(ThinkCartPricingEngine.ToCartItem).ToList(),
            Subtotal = subtotal,
            CouponCode = outcome.Coupon?.Code,
            CouponDiscount = outcome.Discount,
            CouponMessage = outcome.Message,
            ShippingZone = new QuoteZone { ZoneId = zone.ZoneId, Code = zone.Code, Name = zone.Name },
            ShippingFee = fee,
            TotalWeightG = weight,
            GrandTotal = subtotal - outcome.Discount + fee
        };
    }

    public async Task<PlaceOrderOutcome> PlaceOrderAsync(
        int organizationId, int? customerId, PlaceOrderRequest request, CancellationToken cancellationToken = default)
    {
        var name = Clean(request.CustomerName) ?? throw new BusinessRuleException("Enter the customer's name.");
        var mobile = ThinkCartRules.NormalizeMobile(request.Phone)
                     ?? throw new BusinessRuleException("Enter a valid mobile number (01XXXXXXXXX).");
        var address = Clean(request.Address) ?? throw new BusinessRuleException("Enter the delivery address.");
        var email = Clean(request.Email)?.ToLowerInvariant();
        if (email is not null && !EmailPattern().IsMatch(email))
            throw new BusinessRuleException("Enter a valid email address.");
        if (request.CartId == Guid.Empty)
            throw new BusinessRuleException("cartId is required.");

        var methodCode = Clean(request.PaymentMethodCode)?.ToLowerInvariant();
        var method = (await checkout.GetPaymentMethodsAsync(cancellationToken))
                         .FirstOrDefault(option => option.Code == methodCode)
                     ?? throw new BusinessRuleException("Choose an available payment method.");

        var settings = await catalog.GetSiteSettingsAsync(CheckoutKeys, cancellationToken);
        if (customerId is null && !ThinkCartSiteSettings.GuestCheckout(settings))
            throw new BusinessRuleException("Sign in to place an order.");

        var zone = await ResolveZoneAsync(request.DistrictId, request.AreaId, settings, cancellationToken);
        var code = CleanCode(request.CouponCode);
        var now = DateTime.UtcNow;

        var command = new PlaceOrderCommand
        {
            OrganizationId = organizationId,
            CartId = request.CartId,
            SignedInCustomerId = customerId,
            CustomerName = name,
            Mobile = mobile,
            Email = email,
            DistrictId = request.DistrictId,
            AreaId = request.AreaId,
            Address = address,
            Note = Clean(request.Note),
            PaymentMethodCode = method.Code,
            CouponCode = code,
            ShippingZoneId = zone.ZoneId
        };

        return await checkout.PlaceOrderAsync(command, input => Price(input, zone, code, now), cancellationToken);
    }

    public async Task<OrderDetail?> TrackOrderAsync(
        int organizationId, string? orderNumber, string? phone, CancellationToken cancellationToken = default)
    {
        var number = Clean(orderNumber)?.ToUpperInvariant();
        var mobile = ThinkCartRules.NormalizeMobile(phone);
        if (number is null || mobile is null)
            return null;

        return await orders.GetOrderAsync(organizationId, number, null, mobile, cancellationToken);
    }

    /// <summary>
    /// Runs inside the place-order transaction on the lines as they are now: every line must be on sale, in
    /// stock and within its order limits (409 otherwise), the coupon must still be valid for this customer
    /// (400 with the reason), and the shipping fee comes from the zone's weight band.
    /// </summary>
    private static OrderPricingResult Price(OrderPricingInput input, ShippingResolution zone, string? couponCode, DateTime nowUtc)
    {
        var lines = input.Lines;
        if (lines.Count == 0)
            throw new BusinessRuleException("The cart is empty.");

        var unavailable = lines.Where(line => !line.CanSupply(line.Quantity)).ToList();
        if (unavailable.Count > 0)
            throw new BusinessConflictException(
                "Out of stock: " + string.Join(", ", unavailable.Select(ThinkCartPricingEngine.Describe)) +
                ". Update the cart and try again.");

        var outsideLimits = lines
            .Where(line => line.Quantity < line.MinOrderQty || line.MaxOrderQty is int max && max > 0 && line.Quantity > max)
            .ToList();
        if (outsideLimits.Count > 0)
            throw new BusinessConflictException(
                "The order quantity limits changed for: " +
                string.Join(", ", outsideLimits.Select(ThinkCartPricingEngine.Describe)) + ". Update the cart and try again.");

        var subtotal = ThinkCartPricingEngine.Subtotal(lines);
        var weight = ThinkCartPricingEngine.TotalWeight(lines);
        var coupon = ThinkCartPricingEngine.EvaluateCoupon(couponCode, input.Coupon, lines, subtotal, nowUtc);
        if (couponCode is not null && !coupon.Applied)
            throw new BusinessRuleException(coupon.Message ?? "This coupon code isn't valid.");

        return new OrderPricingResult
        {
            Subtotal = subtotal,
            TotalWeightG = weight,
            CouponId = coupon.Coupon?.Id,
            CouponCode = coupon.Coupon?.Code,
            CouponDiscount = coupon.Discount,
            ShippingFee = coupon.FreeShipping ? 0 : ThinkCartPricingEngine.ShippingFee(zone, weight, subtotal),
            CouponShares = coupon.Shares
        };
    }

    /// <summary>The area's zone, else the district's, else site_setting.shipping.default_zone_code.</summary>
    private async Task<ShippingResolution> ResolveZoneAsync(
        short districtId, int? areaId, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken)
    {
        var location = await checkout.GetDeliveryLocationAsync(districtId, areaId, cancellationToken)
                       ?? throw new BusinessRuleException("Choose a delivery district.");
        if (areaId is not null && location.AreaId is null)
            throw new BusinessRuleException("The chosen area isn't in this district.");

        var zoneId = location.AreaZoneId ?? location.DistrictZoneId;
        var zone = zoneId is null ? null : await checkout.GetShippingZoneAsync(zoneId, null, cancellationToken);
        zone ??= await checkout.GetShippingZoneAsync(null, ThinkCartSiteSettings.DefaultZoneCode(settings), cancellationToken);
        return zone ?? throw new BusinessRuleException("Delivery to this address isn't available yet. Please call us to order.");
    }

    private static string? CleanCode(string? code) => Clean(code)?.ToUpperInvariant();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(ThinkCartRules.EmailPattern, RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
