using Dapper;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.ThinkCart;
using ThinkTechErpRepository.Database;

namespace ThinkTechErpRepository.ThinkCart;

public sealed class ThinkCartOrderRepository(IDbConnectionFactory connectionFactory) : IThinkCartOrderRepository
{
    private const string SummaryColumns = """
        o.order_number AS OrderNumber,
        o.placed_at AS PlacedAt,
        o.status AS Status,
        o.payment_status AS PaymentStatus,
        o.payment_method_code AS PaymentMethodCode,
        (SELECT coalesce(sum(oi.quantity), 0)::int FROM public.ecom_order_item oi WHERE oi.order_id = o.id) AS ItemCount,
        o.grand_total AS GrandTotal
        """;

    public async Task<PagedResult<OrderSummary>> GetCustomerOrdersAsync(
        int organizationId, int customerId, string? status, int limit, int offset,
        CancellationToken cancellationToken = default)
    {
        const string filter = """
            FROM public.ecom_order o
            JOIN public.supplier s ON s.supplier_id = o.customer_id AND s.organization_id = @Org
            WHERE o.customer_id = @CustomerId
              AND (CAST(@Status AS text) IS NULL OR o.status = @Status)
            """;

        await using var connection = connectionFactory.CreateConnection();
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT count(*) {filter};
            SELECT {SummaryColumns} {filter}
            ORDER BY o.placed_at DESC, o.id DESC
            LIMIT @Limit OFFSET @Offset;
            """, new { Org = organizationId, CustomerId = customerId, Status = status, Limit = limit, Offset = offset },
            cancellationToken: cancellationToken));
        var total = await results.ReadSingleAsync<long>();
        var items = (await results.ReadAsync<OrderSummary>()).AsList();
        return new PagedResult<OrderSummary> { Items = items, TotalCount = (int)total, Limit = limit, Offset = offset };
    }

    public async Task<OrderDetail?> GetOrderAsync(
        int organizationId, string orderNumber, int? customerId, string? shippingPhone,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var header = await connection.QuerySingleOrDefaultAsync<OrderHeader>(new CommandDefinition($"""
            SELECT o.id AS Id, {SummaryColumns},
                   o.subtotal AS Subtotal, o.coupon_discount AS CouponDiscount, o.manual_discount AS ManualDiscount,
                   o.shipping_fee AS ShippingFee, o.paid_amount AS PaidAmount,
                   o.shipping_name AS ShippingName, o.shipping_phone AS ShippingPhone,
                   o.shipping_address AS ShippingAddress, d.name AS DistrictName, a.name AS AreaName
            FROM public.ecom_order o
            JOIN public.supplier s ON s.supplier_id = o.customer_id AND s.organization_id = @Org
            JOIN public.district d ON d.id = o.shipping_district_id
            LEFT JOIN public.area a ON a.id = o.shipping_area_id
            WHERE o.order_number = @OrderNumber
              AND (CAST(@CustomerId AS integer) IS NULL OR o.customer_id = @CustomerId)
              AND (CAST(@Phone AS text) IS NULL OR o.shipping_phone = @Phone)
              AND (CAST(@CustomerId AS integer) IS NOT NULL OR CAST(@Phone AS text) IS NOT NULL)
            """, new { Org = organizationId, OrderNumber = orderNumber, CustomerId = customerId, Phone = shippingPhone },
            cancellationToken: cancellationToken));
        if (header is null)
            return null;

        using var results = await connection.QueryMultipleAsync(new CommandDefinition("""
            SELECT oi.product_name AS ProductName, oi.variant_title AS VariantTitle, oi.sku AS Sku,
                   CASE WHEN l.web_status = 'active' AND l.is_active THEN l.slug END AS Slug,
                   coalesce(
                       (SELECT pi.url FROM public.product_image pi
                        WHERE pi.product_variant_id = oi.product_variant_id
                        ORDER BY pi.is_primary DESC, pi.sort_order, pi.id LIMIT 1),
                       sp.image_url,
                       (SELECT pi.url FROM public.product_image pi
                        WHERE pi.product_id = l.product_id
                        ORDER BY pi.is_primary DESC, (pi.product_variant_id IS NULL) DESC, pi.sort_order, pi.id LIMIT 1),
                       l.image_url) AS ImageUrl,
                   oi.quantity AS Quantity, oi.unit_price AS UnitPrice, oi.line_total AS LineTotal
            FROM public.ecom_order_item oi
            LEFT JOIN public.product sp ON sp.product_id = oi.product_id
            LEFT JOIN public.product l ON l.product_id = coalesce(sp.parent_product_id, sp.product_id)
            WHERE oi.order_id = @OrderId
            ORDER BY oi.id;

            SELECT to_status AS Status, changed_at AS At, note AS Note
            FROM public.ecom_order_status_history
            WHERE order_id = @OrderId
            ORDER BY changed_at, id;

            SELECT c.name AS CourierName, sh.tracking_code AS TrackingCode, sh.status AS Status,
                   CASE WHEN c.tracking_url_template IS NOT NULL AND sh.tracking_code IS NOT NULL
                        THEN replace(c.tracking_url_template, '{tracking_code}', sh.tracking_code) END AS TrackingUrl
            FROM public.shipment sh
            JOIN public.courier c ON c.id = sh.courier_id
            WHERE sh.order_id = @OrderId
            ORDER BY sh.created_at, sh.id;
            """, new { OrderId = header.Id }, cancellationToken: cancellationToken));

        return new OrderDetail
        {
            OrderNumber = header.OrderNumber,
            PlacedAt = header.PlacedAt,
            Status = header.Status,
            PaymentStatus = header.PaymentStatus,
            PaymentMethodCode = header.PaymentMethodCode,
            ItemCount = header.ItemCount,
            GrandTotal = header.GrandTotal,
            Items = (await results.ReadAsync<OrderItemView>()).AsList(),
            Subtotal = header.Subtotal,
            CouponDiscount = header.CouponDiscount,
            ManualDiscount = header.ManualDiscount,
            ShippingFee = header.ShippingFee,
            PaidAmount = header.PaidAmount,
            Shipping = new OrderShipping
            {
                Name = header.ShippingName,
                Phone = header.ShippingPhone,
                Address = header.ShippingAddress,
                DistrictName = header.DistrictName,
                AreaName = header.AreaName
            },
            Timeline = (await results.ReadAsync<OrderTimelineEntry>()).AsList(),
            Shipments = (await results.ReadAsync<OrderShipmentView>()).AsList()
        };
    }

    public async Task<AccountStats> GetStatsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<AccountStats>(new CommandDefinition("""
            SELECT count(*)::int AS TotalOrders,
                   count(*) FILTER (WHERE status = ANY(@Open))::int AS PendingOrders,
                   count(*) FILTER (WHERE status = 'delivered')::int AS DeliveredOrders,
                   count(*) FILTER (WHERE status = 'cancelled')::int AS CancelledOrders,
                   coalesce(sum(grand_total) FILTER (WHERE status = 'delivered'), 0) AS TotalSpent,
                   (SELECT count(*)::int FROM public.wishlist_item w WHERE w.customer_id = @CustomerId) AS WishlistCount
            FROM public.ecom_order
            WHERE customer_id = @CustomerId
            """, new { CustomerId = customerId, Open = EcomOrderStatuses.Open.ToArray() },
            cancellationToken: cancellationToken));
    }

    private sealed class OrderHeader
    {
        public long Id { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public DateTime PlacedAt { get; set; }
        public string Status { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = string.Empty;
        public string PaymentMethodCode { get; set; } = string.Empty;
        public int ItemCount { get; set; }
        public decimal GrandTotal { get; set; }
        public decimal Subtotal { get; set; }
        public decimal CouponDiscount { get; set; }
        public decimal ManualDiscount { get; set; }
        public decimal ShippingFee { get; set; }
        public decimal PaidAmount { get; set; }
        public string ShippingName { get; set; } = string.Empty;
        public string ShippingPhone { get; set; } = string.Empty;
        public string ShippingAddress { get; set; } = string.Empty;
        public string DistrictName { get; set; } = string.Empty;
        public string? AreaName { get; set; }
    }
}
