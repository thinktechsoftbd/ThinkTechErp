namespace ThinkTechErpDomain.Models.ThinkCart;

public class OrderSummary
{
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime PlacedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string PaymentMethodCode { get; set; } = string.Empty;

    /// <summary>Σ quantity of the order's lines.</summary>
    public int ItemCount { get; set; }

    public decimal GrandTotal { get; set; }
}

public sealed class OrderDetail : OrderSummary
{
    public IReadOnlyList<OrderItemView> Items { get; set; } = [];
    public decimal Subtotal { get; set; }
    public decimal CouponDiscount { get; set; }
    public decimal ManualDiscount { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal PaidAmount { get; set; }
    public OrderShipping Shipping { get; set; } = new();
    public IReadOnlyList<OrderTimelineEntry> Timeline { get; set; } = [];
    public IReadOnlyList<OrderShipmentView> Shipments { get; set; } = [];
}

public sealed class OrderItemView
{
    public string ProductName { get; set; } = string.Empty;
    public string VariantTitle { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string? ImageUrl { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class OrderShipping
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string DistrictName { get; set; } = string.Empty;
    public string? AreaName { get; set; }
}

public sealed class OrderTimelineEntry
{
    public string Status { get; set; } = string.Empty;
    public DateTime At { get; set; }
    public string? Note { get; set; }
}

public sealed class OrderShipmentView
{
    public string CourierName { get; set; } = string.Empty;
    public string? TrackingCode { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? TrackingUrl { get; set; }
}

public sealed class AccountDashboard
{
    public CustomerProfile Profile { get; set; } = new();
    public AccountStats Stats { get; set; } = new();
    public IReadOnlyList<OrderSummary> RecentOrders { get; set; } = [];
}

public sealed class AccountStats
{
    public int TotalOrders { get; set; }

    /// <summary>Placed and not delivered, cancelled or returned yet.</summary>
    public int PendingOrders { get; set; }

    public int DeliveredOrders { get; set; }
    public int CancelledOrders { get; set; }

    /// <summary>Σ grand_total of delivered orders.</summary>
    public decimal TotalSpent { get; set; }

    public int WishlistCount { get; set; }
}
