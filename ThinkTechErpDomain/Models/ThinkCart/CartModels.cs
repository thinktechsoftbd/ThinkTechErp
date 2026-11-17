using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.ThinkCart;

public sealed class Cart
{
    /// <summary>cart.object_id, kept by the storefront in a cookie.</summary>
    public Guid CartId { get; set; }

    public IReadOnlyList<CartItem> Items { get; set; } = [];

    /// <summary>Σ quantity.</summary>
    public int ItemCount { get; set; }

    public decimal Subtotal { get; set; }
}

public sealed class CartItem
{
    public long VariantId { get; set; }

    /// <summary>The listing product (the one the slug and name belong to).</summary>
    public int ProductId { get; set; }

    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string VariantTitle { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public decimal AvailableQuantity { get; set; }
    public bool InStock { get; set; }
    public int? MaxOrderQty { get; set; }
}

public sealed class CartCreateRequest
{
    [MaxLength(100)]
    public List<CartLineInput>? Items { get; set; }
}

public sealed class CartLineInput
{
    [Range(1, long.MaxValue)]
    public long VariantId { get; set; }

    [Range(0, ThinkCartRules.MaxCartQuantity)]
    public int Quantity { get; set; }
}

public sealed class CartContactRequest
{
    [StringLength(ThinkCartRules.NameMaxLength)]
    public string? Name { get; set; }

    [StringLength(30)]
    public string? Phone { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }
}

/// <summary>
/// One sellable size (product_variant + its ERP product + the listing product) priced now: the live
/// campaign price or the size's sale price, and stock from stock_balance in the organization.
/// </summary>
public sealed class VariantLine
{
    public long VariantId { get; set; }
    public int SizeProductId { get; set; }
    public int ListingId { get; set; }
    public string? Slug { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public decimal? PackSize { get; set; }
    public string? PackUnit { get; set; }
    public int WeightG { get; set; }

    /// <summary>product.sale_price of the size (list_price on the order line).</summary>
    public decimal ListPrice { get; set; }

    /// <summary>The effective price: a live campaign's sale price, else ListPrice.</summary>
    public decimal Price { get; set; }

    public int? CampaignId { get; set; }
    public decimal? CompareAtRaw { get; set; }
    public decimal AvailableQuantity { get; set; }
    public bool AllowBackorder { get; set; }
    public string ProductType { get; set; } = "Stock";
    public int MinOrderQty { get; set; } = 1;
    public int? MaxOrderQty { get; set; }
    public bool IsDefault { get; set; }
    public bool IsVisible { get; set; }
    public int? CategoryId { get; set; }
    public decimal? UnitCost { get; set; }
    public string? ImageUrl { get; set; }

    /// <summary>On cart lines: the quantity in the cart.</summary>
    public int Quantity { get; set; }

    public bool TracksStock => ProductType == "Stock" && !AllowBackorder;

    /// <summary>Visible and (in stock, on backorder, or not a stock item).</summary>
    public bool InStock => IsVisible && (AvailableQuantity > 0 || !TracksStock);

    public bool CanSupply(int quantity) => IsVisible && (!TracksStock || AvailableQuantity >= quantity);

    public decimal LineTotal => ThinkCartPricing.Money(Price * Quantity);
}

public sealed class CheckoutOptions
{
    public IReadOnlyList<PaymentMethodOption> PaymentMethods { get; set; } = [];
    public IReadOnlyList<ShippingZoneOptionView> ShippingZones { get; set; } = [];
    public string DefaultZoneCode { get; set; } = string.Empty;
}

public sealed class PaymentMethodOption
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string MethodType { get; set; } = string.Empty;
}

public sealed class ShippingZoneOptionView
{
    public short ZoneId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public short? MinDeliveryDays { get; set; }
    public short? MaxDeliveryDays { get; set; }
}

public sealed class LocationDistrict
{
    public short DistrictId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameBn { get; set; }
    public string DivisionName { get; set; } = string.Empty;
    public short? ShippingZoneId { get; set; }
}

public sealed class LocationArea
{
    public int AreaId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameBn { get; set; }
    public short? ShippingZoneId { get; set; }
}

public sealed class CheckoutQuoteRequest
{
    public Guid CartId { get; set; }

    [Range(1, short.MaxValue)]
    public short DistrictId { get; set; }

    [Range(1, int.MaxValue)]
    public int? AreaId { get; set; }

    [StringLength(50)]
    public string? CouponCode { get; set; }
}

public sealed class CheckoutQuote
{
    public IReadOnlyList<CartItem> Items { get; set; } = [];
    public decimal Subtotal { get; set; }
    public string? CouponCode { get; set; }
    public decimal CouponDiscount { get; set; }

    /// <summary>Why the coupon was rejected; null when it applied or none was given.</summary>
    public string? CouponMessage { get; set; }

    public QuoteZone ShippingZone { get; set; } = new();
    public decimal ShippingFee { get; set; }
    public int TotalWeightG { get; set; }
    public decimal GrandTotal { get; set; }
}

public sealed class QuoteZone
{
    public short ZoneId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class PlaceOrderRequest
{
    public Guid CartId { get; set; }

    [Required, StringLength(ThinkCartRules.NameMaxLength, MinimumLength = 1)]
    public string CustomerName { get; set; } = string.Empty;

    [Required, StringLength(30)]
    public string Phone { get; set; } = string.Empty;

    [StringLength(ThinkCartRules.EmailMaxLength)]
    public string? Email { get; set; }

    [Range(1, short.MaxValue)]
    public short DistrictId { get; set; }

    [Range(1, int.MaxValue)]
    public int? AreaId { get; set; }

    [Required, StringLength(ThinkCartRules.AddressMaxLength, MinimumLength = 1)]
    public string Address { get; set; } = string.Empty;

    [StringLength(ThinkCartRules.NoteMaxLength)]
    public string? Note { get; set; }

    [Required, StringLength(40)]
    public string PaymentMethodCode { get; set; } = string.Empty;

    [StringLength(50)]
    public string? CouponCode { get; set; }
}

public sealed class OrderPlaced
{
    public string OrderNumber { get; set; } = string.Empty;
    public Guid ObjectId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentMethodCode { get; set; } = string.Empty;
    public decimal GrandTotal { get; set; }
}

/// <summary>The zone an address ships in and that zone's active weight bands.</summary>
public sealed class ShippingResolution
{
    public short ZoneId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public IReadOnlyList<ShippingRateBand> Rates { get; set; } = [];
}

public sealed class ShippingRateBand
{
    public int MinWeightG { get; set; }
    public int? MaxWeightG { get; set; }
    public decimal Fee { get; set; }
    public decimal? FreeShippingMinOrder { get; set; }
}

public sealed class DeliveryLocation
{
    public short DistrictId { get; set; }
    public short? DistrictZoneId { get; set; }
    public int? AreaId { get; set; }
    public short? AreaZoneId { get; set; }
}

/// <summary>A coupon with its usage so far, read for one cart (and customer, when known).</summary>
public sealed class CouponInfo
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DiscountType { get; set; } = string.Empty;
    public decimal DiscountValue { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public decimal MinOrderAmount { get; set; }
    public string AppliesTo { get; set; } = "all";
    public bool FirstOrderOnly { get; set; }
    public int? UsageLimit { get; set; }
    public int? UsageLimitPerCustomer { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }

    /// <summary>Orders (not cancelled) that used it.</summary>
    public int TimesUsed { get; set; }

    /// <summary>Orders (not cancelled) of this customer that used it; null when the customer isn't known yet.</summary>
    public int? TimesUsedByCustomer { get; set; }

    /// <summary>Orders (not cancelled) the customer placed before; null when the customer isn't known yet.</summary>
    public int? CustomerOrderCount { get; set; }

    /// <summary>Listing products in the cart the coupon applies to (every one for applies_to 'all').</summary>
    public IReadOnlyList<int> EligibleListingIds { get; set; } = [];
}

/// <summary>What place-order hands the pricing rules inside its transaction.</summary>
public sealed class OrderPricingInput
{
    public IReadOnlyList<VariantLine> Lines { get; set; } = [];
    public int CustomerId { get; set; }
    public CouponInfo? Coupon { get; set; }
}

/// <summary>What the pricing rules hand back: the money, and the coupon share per line (by variant).</summary>
public sealed class OrderPricingResult
{
    public decimal Subtotal { get; set; }
    public int TotalWeightG { get; set; }
    public int? CouponId { get; set; }
    public string? CouponCode { get; set; }
    public decimal CouponDiscount { get; set; }
    public decimal ShippingFee { get; set; }
    public IReadOnlyDictionary<long, decimal> CouponShares { get; set; } = new Dictionary<long, decimal>();
}

/// <summary>A validated order, ready to be written.</summary>
public sealed class PlaceOrderCommand
{
    public int OrganizationId { get; set; }
    public Guid CartId { get; set; }
    public int? SignedInCustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string? Email { get; set; }
    public short DistrictId { get; set; }
    public int? AreaId { get; set; }
    public string Address { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string PaymentMethodCode { get; set; } = string.Empty;
    public string? CouponCode { get; set; }
    public short ShippingZoneId { get; set; }
}

public enum CartLookupStatus
{
    Found,
    NotFound,
    Converted
}
