using System.Globalization;
using System.Text.Json;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.ThinkCart;

namespace ThinkTechErpService.ThinkCart;

/// <summary>The result of checking a coupon against a cart.</summary>
internal sealed record CouponOutcome(
    CouponInfo? Coupon, decimal Discount, bool FreeShipping, string? Message, IReadOnlyDictionary<long, decimal> Shares)
{
    public static readonly CouponOutcome None = new(null, 0, false, null, new Dictionary<long, decimal>());

    public bool Applied => Coupon is not null;

    public static CouponOutcome Rejected(string message) => new(null, 0, false, message, new Dictionary<long, decimal>());
}

/// <summary>Cart, quote and order money rules: line totals, shipping bands and coupons.</summary>
internal static class ThinkCartPricingEngine
{
    public static decimal Subtotal(IEnumerable<VariantLine> lines) => lines.Sum(line => line.LineTotal);

    public static int TotalWeight(IEnumerable<VariantLine> lines) => lines.Sum(line => line.WeightG * line.Quantity);

    /// <summary>
    /// The fee of the weight band the parcel falls in (min ≤ weight ≤ max, the lighter band on a boundary), or
    /// the heaviest band when it is heavier than all of them; free when the subtotal reaches that band's
    /// free_shipping_min_order.
    /// </summary>
    public static decimal ShippingFee(ShippingResolution zone, int weightG, decimal subtotal)
    {
        if (zone.Rates.Count == 0)
            throw new BusinessRuleException(
                $"Delivery charges for {zone.Name} aren't set up yet. Please call us to order.");

        var band = zone.Rates
                       .Where(rate => rate.MinWeightG <= weightG && (rate.MaxWeightG is null || weightG <= rate.MaxWeightG))
                       .OrderBy(rate => rate.MinWeightG)
                       .FirstOrDefault()
                   ?? (weightG < zone.Rates.Min(rate => rate.MinWeightG)
                       ? zone.Rates.OrderBy(rate => rate.MinWeightG).First()
                       : zone.Rates.OrderByDescending(rate => rate.MaxWeightG ?? int.MaxValue)
                           .ThenByDescending(rate => rate.MinWeightG).First());

        return band.FreeShippingMinOrder is decimal freeFrom && subtotal >= freeFrom ? 0 : ThinkCartPricing.Money(band.Fee);
    }

    /// <summary>
    /// Checks a coupon (active, dates, total and per-customer limits, first order, minimum order, what it applies
    /// to) and works out its discount: percentage of the eligible lines (capped by max_discount_amount), a fixed
    /// amount (at most the eligible lines), or free shipping. The discount is spread over the eligible lines
    /// pro rata (by line total), the last line taking the rounding remainder.
    /// </summary>
    public static CouponOutcome EvaluateCoupon(
        string? requestedCode, CouponInfo? coupon, IReadOnlyList<VariantLine> lines, decimal subtotal, DateTime nowUtc)
    {
        if (requestedCode is null)
            return CouponOutcome.None;
        if (coupon is null)
            return CouponOutcome.Rejected("This coupon code isn't valid.");
        if (coupon.StartsAt is DateTime startsAt && startsAt > nowUtc)
            return CouponOutcome.Rejected("This coupon isn't active yet.");
        if (coupon.EndsAt is DateTime endsAt && endsAt <= nowUtc)
            return CouponOutcome.Rejected("This coupon has expired.");
        if (coupon.UsageLimit is int limit && coupon.TimesUsed >= limit)
            return CouponOutcome.Rejected("This coupon has been fully redeemed.");
        if (coupon.UsageLimitPerCustomer is int perCustomer && coupon.TimesUsedByCustomer is int used && used >= perCustomer)
            return CouponOutcome.Rejected("You have already used this coupon.");
        if (coupon.FirstOrderOnly && coupon.CustomerOrderCount is int orders && orders > 0)
            return CouponOutcome.Rejected("This coupon is for a first order only.");
        if (subtotal < coupon.MinOrderAmount)
            return CouponOutcome.Rejected(
                $"This coupon needs an order of at least {coupon.MinOrderAmount.ToString("0.##", CultureInfo.InvariantCulture)} BDT.");

        var eligibleIds = coupon.EligibleListingIds.ToHashSet();
        var eligible = lines.Where(line => eligibleIds.Contains(line.ListingId) && line.LineTotal > 0).ToList();
        var eligibleTotal = eligible.Sum(line => line.LineTotal);
        if (eligibleTotal <= 0)
            return CouponOutcome.Rejected("This coupon doesn't apply to the items in your cart.");

        if (coupon.DiscountType == "free_shipping")
            return new CouponOutcome(coupon, 0, true, null, new Dictionary<long, decimal>());

        var discount = coupon.DiscountType == "percentage"
            ? ThinkCartPricing.Money(eligibleTotal * coupon.DiscountValue / 100m)
            : ThinkCartPricing.Money(Math.Min(coupon.DiscountValue, eligibleTotal));
        if (coupon.DiscountType == "percentage" && coupon.MaxDiscountAmount is decimal cap)
            discount = Math.Min(discount, ThinkCartPricing.Money(cap));
        discount = Math.Min(discount, subtotal);

        var shares = new Dictionary<long, decimal>();
        var allocated = 0m;
        for (var index = 0; index < eligible.Count; index++)
        {
            var line = eligible[index];
            var share = index == eligible.Count - 1
                ? discount - allocated
                : ThinkCartPricing.Money(discount * line.LineTotal / eligibleTotal);
            shares[line.VariantId] = share;
            allocated += share;
        }

        return new CouponOutcome(coupon, discount, false, null, shares);
    }

    /// <summary>The quantity the order limits allow: at least min_order_qty and at most max_order_qty.</summary>
    public static int ClampQuantity(VariantLine line, int quantity)
    {
        if (quantity < line.MinOrderQty)
            quantity = line.MinOrderQty;
        if (line.MaxOrderQty is int max && max > 0 && quantity > max)
            quantity = max;
        return quantity;
    }

    /// <summary>409 unless the size is on sale and can supply the quantity.</summary>
    public static void EnsureAvailable(VariantLine line, int quantity)
    {
        var name = Describe(line);
        if (!line.IsVisible)
            throw new BusinessConflictException($"{name} is no longer available.");
        if (!line.InStock)
            throw new BusinessConflictException($"{name} is out of stock.");
        if (!line.CanSupply(quantity))
            throw new BusinessConflictException(
                $"Only {ThinkCartPricing.Quantity(Math.Max(line.AvailableQuantity, 0)).ToString(CultureInfo.InvariantCulture)} of {name} left in stock.");
    }

    public static string Describe(VariantLine line)
        => string.IsNullOrWhiteSpace(line.Title) || line.Title == "Default" ? line.Name : $"{line.Name} ({line.Title})";

    public static CartItem ToCartItem(VariantLine line) => new()
    {
        VariantId = line.VariantId,
        ProductId = line.ListingId,
        Slug = line.Slug ?? string.Empty,
        Name = line.Name,
        VariantTitle = line.Title,
        ImageUrl = line.ImageUrl,
        UnitPrice = line.Price,
        CompareAtPrice = ThinkCartPricing.CompareAt(line.Price, line.CompareAtRaw),
        Quantity = line.Quantity,
        LineTotal = line.LineTotal,
        AvailableQuantity = ThinkCartPricing.Quantity(Math.Max(line.AvailableQuantity, 0)),
        InStock = line.CanSupply(Math.Max(line.Quantity, 1)),
        MaxOrderQty = line.MaxOrderQty
    };

    public static Cart ToCart(Guid cartId, IReadOnlyList<VariantLine> lines) => new()
    {
        CartId = cartId,
        Items = lines.Select(ToCartItem).ToList(),
        ItemCount = lines.Sum(line => line.Quantity),
        Subtotal = Subtotal(lines)
    };
}

/// <summary>Reads the site_setting JSON values the storefront uses.</summary>
internal static class ThinkCartSiteSettings
{
    public const string Store = "store";
    public const string Checkout = "checkout";
    public const string Shipping = "shipping";

    public static SiteInfo BuildSiteInfo(IReadOnlyDictionary<string, string> settings, string? organizationName)
    {
        using var store = Parse(settings, Store);
        using var checkout = Parse(settings, Checkout);
        var storeRoot = store?.RootElement;
        var social = storeRoot is { } root && root.TryGetProperty("social", out var nested) && nested.ValueKind == JsonValueKind.Object
            ? nested
            : storeRoot;

        return new SiteInfo
        {
            Name = Text(storeRoot, "name") ?? organizationName ?? "ThinkCart",
            Hotline = Text(storeRoot, "hotline"),
            Whatsapp = Text(storeRoot, "whatsapp"),
            Email = Text(storeRoot, "email"),
            Address = Text(storeRoot, "address"),
            Social = new SiteSocial
            {
                Facebook = Text(social, "facebook"),
                Instagram = Text(social, "instagram"),
                Youtube = Text(social, "youtube"),
                Tiktok = Text(social, "tiktok")
            },
            GuestCheckout = Flag(checkout?.RootElement, "guest_checkout") ?? true,
            OtpRequired = Flag(checkout?.RootElement, "otp_required") ?? false
        };
    }

    public static bool GuestCheckout(IReadOnlyDictionary<string, string> settings)
    {
        using var checkout = Parse(settings, Checkout);
        return Flag(checkout?.RootElement, "guest_checkout") ?? true;
    }

    public static string DefaultZoneCode(IReadOnlyDictionary<string, string> settings)
    {
        using var shipping = Parse(settings, Shipping);
        return Text(shipping?.RootElement, "default_zone_code") ?? ThinkCartRules.FallbackZoneCode;
    }

    private static JsonDocument? Parse(IReadOnlyDictionary<string, string> settings, string key)
    {
        if (!settings.TryGetValue(key, out var json))
            return null;
        try
        {
            var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                return document;
            document.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement? element, string property)
        => element is { ValueKind: JsonValueKind.Object } value &&
           value.TryGetProperty(property, out var item) &&
           item.ValueKind == JsonValueKind.String &&
           !string.IsNullOrWhiteSpace(item.GetString())
            ? item.GetString()!.Trim()
            : null;

    private static bool? Flag(JsonElement? element, string property)
        => element is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(property, out var item)
            ? item.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            }
            : null;
}
