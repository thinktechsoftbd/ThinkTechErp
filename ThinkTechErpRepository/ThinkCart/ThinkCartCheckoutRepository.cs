using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.ThinkCart;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.ThinkCart;

public sealed class ThinkCartCheckoutRepository(IDbConnectionFactory connectionFactory) : IThinkCartCheckoutRepository
{
    public async Task<IReadOnlyList<PaymentMethodOption>> GetPaymentMethodsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<PaymentMethodOption>(new CommandDefinition("""
            SELECT code AS Code, name AS Name, method_type AS MethodType
            FROM public.payment_method
            WHERE is_active
            ORDER BY sort_order, code
            """, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ShippingZoneOptionView>> GetShippingZonesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ShippingZoneOptionView>(new CommandDefinition("""
            SELECT id AS ZoneId, code AS Code, name AS Name, min_delivery_days AS MinDeliveryDays,
                   max_delivery_days AS MaxDeliveryDays
            FROM public.shipping_zone
            WHERE is_active
            ORDER BY id
            """, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<LocationDistrict>> GetDistrictsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<LocationDistrict>(new CommandDefinition("""
            SELECT d.id AS DistrictId, d.name AS Name, d.name_bn AS NameBn, v.name AS DivisionName,
                   d.shipping_zone_id AS ShippingZoneId
            FROM public.district d
            JOIN public.division v ON v.id = d.division_id
            WHERE d.is_active AND v.is_active
            ORDER BY d.name, d.id
            """, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<LocationArea>?> GetAreasAsync(short districtId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        using var results = await connection.QueryMultipleAsync(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1 FROM public.district d JOIN public.division v ON v.id = d.division_id
                WHERE d.id = @DistrictId AND d.is_active AND v.is_active);

            SELECT id AS AreaId, name AS Name, name_bn AS NameBn, shipping_zone_id AS ShippingZoneId
            FROM public.area
            WHERE district_id = @DistrictId AND is_active
            ORDER BY name, id;
            """, new { DistrictId = districtId }, cancellationToken: cancellationToken));
        var exists = await results.ReadSingleAsync<bool>();
        var areas = (await results.ReadAsync<LocationArea>()).AsList();
        return exists ? areas : null;
    }

    public async Task<DeliveryLocation?> GetDeliveryLocationAsync(
        short districtId, int? areaId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<DeliveryLocation>(new CommandDefinition("""
            SELECT d.id AS DistrictId, d.shipping_zone_id AS DistrictZoneId, a.id AS AreaId, a.shipping_zone_id AS AreaZoneId
            FROM public.district d
            JOIN public.division v ON v.id = d.division_id AND v.is_active
            LEFT JOIN public.area a ON a.id = CAST(@AreaId AS integer) AND a.district_id = d.id AND a.is_active
            WHERE d.id = @DistrictId AND d.is_active
            """, new { DistrictId = districtId, AreaId = areaId }, cancellationToken: cancellationToken));
    }

    public async Task<ShippingResolution?> GetShippingZoneAsync(
        short? zoneId, string? code, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var zone = await connection.QuerySingleOrDefaultAsync<ShippingResolution>(new CommandDefinition("""
            SELECT id AS ZoneId, code AS Code, name AS Name
            FROM public.shipping_zone
            WHERE is_active
              AND ((CAST(@ZoneId AS smallint) IS NOT NULL AND id = @ZoneId)
                   OR (CAST(@ZoneId AS smallint) IS NULL AND code = @Code))
            LIMIT 1
            """, new { ZoneId = zoneId, Code = code }, cancellationToken: cancellationToken));
        if (zone is null)
            return null;

        var rates = await connection.QueryAsync<ShippingRateBand>(new CommandDefinition("""
            SELECT min_weight_g AS MinWeightG, max_weight_g AS MaxWeightG, fee AS Fee,
                   free_shipping_min_order AS FreeShippingMinOrder
            FROM public.shipping_rate
            WHERE shipping_zone_id = @ZoneId AND is_active
            ORDER BY min_weight_g, id
            """, new { zone.ZoneId }, cancellationToken: cancellationToken));
        zone.Rates = rates.AsList();
        return zone;
    }

    public async Task<CouponInfo?> GetCouponAsync(
        int organizationId, string code, int? customerId, IReadOnlyList<int> listingIds,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await ReadCouponAsync(connection, null, organizationId, code, customerId, listingIds, false, cancellationToken);
    }

    public async Task<PlaceOrderOutcome> PlaceOrderAsync(
        PlaceOrderCommand command, Func<OrderPricingInput, OrderPricingResult> price,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The lock makes a double submit wait, then see the cart converted.
        var cart = await connection.QuerySingleOrDefaultAsync<CartHeader>(new CommandDefinition("""
            SELECT id AS Id, object_id AS ObjectId, status AS Status, customer_id AS CustomerId
            FROM public.cart
            WHERE object_id = @CartId
            FOR UPDATE
            """, new { command.CartId }, transaction, cancellationToken: cancellationToken));
        if (cart is null)
            return new PlaceOrderOutcome(PlaceOrderStatus.CartNotFound);
        if (!cart.IsOpen)
            return new PlaceOrderOutcome(PlaceOrderStatus.CartConverted);

        var lines = await ThinkCartCartRepository.ReadCartLinesAsync(
            connection, transaction, command.OrganizationId, cart.Id, cancellationToken);

        var customerId = command.SignedInCustomerId
            ?? await FindOrCreateCustomerAsync(connection, transaction, command, cancellationToken);

        var customer = await connection.QuerySingleOrDefaultAsync<CustomerStateRow>(
            new CommandDefinition("""
                SELECT is_active AS IsActive, blocked_at IS NOT NULL AS IsBlocked, email AS Email
                FROM public.supplier
                WHERE supplier_id = @CustomerId AND organization_id = @Org AND is_customer
                FOR UPDATE
                """, new { CustomerId = customerId, Org = command.OrganizationId }, transaction,
                cancellationToken: cancellationToken));
        if (customer is null)
            return new PlaceOrderOutcome(PlaceOrderStatus.CustomerNotFound);
        if (!customer.IsActive || customer.IsBlocked)
            throw new BusinessRuleException("We can't take orders for this number online. Please call us to order.");

        var coupon = command.CouponCode is null
            ? null
            : await ReadCouponAsync(connection, transaction, command.OrganizationId, command.CouponCode, customerId,
                lines.Select(line => line.ListingId).Distinct().ToList(), true, cancellationToken);

        // Checks stock, limits and the coupon, and prices the order; throws to reject it (rolls back).
        var pricing = price(new OrderPricingInput { Lines = lines, CustomerId = customerId, Coupon = coupon });

        if (customer.Email is null && command.Email is not null)
        {
            await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE public.supplier
                SET email = @Email, updated_at = {SecurityDb.NowUtc}
                WHERE supplier_id = @CustomerId AND email IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM public.supplier other
                      WHERE other.organization_id = @Org AND other.is_customer AND other.supplier_id <> @CustomerId
                        AND lower(other.email) = lower(@Email))
                """, new { command.Email, CustomerId = customerId, Org = command.OrganizationId }, transaction,
                cancellationToken: cancellationToken));
        }

        // The address book gets the address when it is new; the first one becomes the default.
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO public.supplier_address
                (supplier_id, recipient_name, recipient_phone, address_line1, area_id, district_id, country_code, is_default)
            SELECT @CustomerId, @Name, @Mobile, @Address, CAST(@AreaId AS integer), @DistrictId, 'BD',
                   NOT EXISTS (SELECT 1 FROM public.supplier_address a WHERE a.supplier_id = @CustomerId AND a.is_default)
            WHERE NOT EXISTS (
                SELECT 1 FROM public.supplier_address a
                WHERE a.supplier_id = @CustomerId
                  AND a.district_id = @DistrictId
                  AND a.area_id IS NOT DISTINCT FROM CAST(@AreaId AS integer)
                  AND lower(btrim(a.address_line1)) = lower(btrim(@Address)))
            """, new
        {
            CustomerId = customerId,
            Name = command.CustomerName,
            command.Mobile,
            command.Address,
            command.AreaId,
            command.DistrictId
        }, transaction, cancellationToken: cancellationToken));

        var order = await connection.QuerySingleAsync<OrderPlacedRow>(new CommandDefinition("""
            INSERT INTO public.ecom_order
                (customer_id, cart_id, channel, status, payment_status, payment_method_code,
                 shipping_name, shipping_phone, shipping_district_id, shipping_area_id, shipping_address,
                 shipping_zone_id, total_weight_g, subtotal, coupon_id, coupon_code, coupon_discount, shipping_fee,
                 customer_note)
            VALUES
                (@CustomerId, @CartId, 'website', 'pending', 'unpaid', @PaymentMethodCode,
                 @Name, @Mobile, @DistrictId, CAST(@AreaId AS integer), @Address,
                 @ZoneId, @TotalWeightG, @Subtotal, @CouponId, @CouponCode, @CouponDiscount, @ShippingFee,
                 @Note)
            RETURNING id AS Id, object_id AS ObjectId, order_number AS OrderNumber, status AS Status,
                      payment_method_code AS PaymentMethodCode, grand_total AS GrandTotal
            """, new
        {
            CustomerId = customerId,
            CartId = cart.Id,
            command.PaymentMethodCode,
            Name = command.CustomerName,
            command.Mobile,
            command.DistrictId,
            command.AreaId,
            command.Address,
            ZoneId = command.ShippingZoneId,
            pricing.TotalWeightG,
            pricing.Subtotal,
            pricing.CouponId,
            pricing.CouponCode,
            pricing.CouponDiscount,
            pricing.ShippingFee,
            command.Note
        }, transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO public.ecom_order_item
                (order_id, product_id, product_variant_id, product_name, variant_title, sku, category_id, quantity,
                 list_price, unit_price, unit_cost, coupon_discount_share, campaign_id)
            SELECT @OrderId, line.product_id, line.variant_id, line.product_name, line.variant_title, line.sku,
                   line.category_id, line.quantity, line.list_price, line.unit_price, line.unit_cost,
                   line.coupon_share, line.campaign_id
            FROM unnest(CAST(@ProductIds AS integer[]), CAST(@VariantIds AS bigint[]), CAST(@ProductNames AS text[]),
                        CAST(@VariantTitles AS text[]), CAST(@Skus AS text[]), CAST(@CategoryIds AS integer[]),
                        CAST(@Quantities AS integer[]), CAST(@ListPrices AS numeric[]), CAST(@UnitPrices AS numeric[]),
                        CAST(@UnitCosts AS numeric[]), CAST(@CouponShares AS numeric[]), CAST(@CampaignIds AS integer[]))
                 WITH ORDINALITY
                 AS line(product_id, variant_id, product_name, variant_title, sku, category_id, quantity,
                         list_price, unit_price, unit_cost, coupon_share, campaign_id, position)
            ORDER BY line.position;

            INSERT INTO public.ecom_order_status_history (order_id, from_status, to_status)
            VALUES (@OrderId, NULL, 'pending');

            UPDATE public.cart
            SET status = 'converted',
                customer_id = coalesce(customer_id, @CustomerId),
                coupon_code = @CouponCode,
                contact_name = @Name,
                contact_phone = @Mobile,
                contact_address = @Address,
                checkout_started_at = coalesce(checkout_started_at, now()),
                updated_at = now()
            WHERE id = @CartId;
            """, new
        {
            OrderId = order.Id,
            ProductIds = lines.Select(line => line.SizeProductId).ToArray(),
            VariantIds = lines.Select(line => line.VariantId).ToArray(),
            ProductNames = lines.Select(line => line.Name).ToArray(),
            VariantTitles = lines.Select(line => line.Title).ToArray(),
            Skus = lines.Select(line => line.Sku).ToArray(),
            CategoryIds = lines.Select(line => line.CategoryId).ToArray(),
            Quantities = lines.Select(line => line.Quantity).ToArray(),
            ListPrices = lines.Select(line => line.ListPrice).ToArray(),
            UnitPrices = lines.Select(line => line.Price).ToArray(),
            UnitCosts = lines.Select(line => line.UnitCost).ToArray(),
            CouponShares = lines.Select(line => pricing.CouponShares.GetValueOrDefault(line.VariantId)).ToArray(),
            CampaignIds = lines.Select(line => line.CampaignId).ToArray(),
            CustomerId = customerId,
            CartId = cart.Id,
            pricing.CouponCode,
            Name = command.CustomerName,
            command.Mobile,
            command.Address
        }, transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return new PlaceOrderOutcome(PlaceOrderStatus.Placed, new OrderPlaced
        {
            OrderNumber = order.OrderNumber,
            ObjectId = order.ObjectId,
            Status = order.Status,
            PaymentMethodCode = order.PaymentMethodCode,
            GrandTotal = order.GrandTotal
        });
    }

    /// <summary>
    /// The organization's customer with this mobile (one with an account first, then the oldest), or a new
    /// guest customer (is_customer, not a supplier, code 'WEB-' || mobile).
    /// </summary>
    private static async Task<int> FindOrCreateCustomerAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, PlaceOrderCommand command,
        CancellationToken cancellationToken)
    {
        var existing = await ThinkCartCustomerRepository.FindCustomerIdByMobileAsync(
            connection, transaction, command.OrganizationId, command.Mobile, cancellationToken);
        if (existing is int id)
            return id;

        // ON CONFLICT: a parallel order for the same new number created the row first (same supplier code).
        var created = await ThinkCartCustomerRepository.InsertCustomerAsync(
            connection, transaction, command.OrganizationId, command.CustomerName, command.Mobile, command.Email,
            passwordHash: null, mobileVerified: false, cancellationToken);
        if (created is int createdId)
            return createdId;

        return await ThinkCartCustomerRepository.FindCustomerIdByMobileAsync(
                   connection, transaction, command.OrganizationId, command.Mobile, cancellationToken)
               ?? throw new BusinessConflictException(
                   $"Supplier code {ThinkCartRules.WebCustomerCodePrefix}{command.Mobile} is already used by a party that isn't a customer.");
    }

    private static async Task<CouponInfo?> ReadCouponAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, int organizationId, string code, int? customerId,
        IReadOnlyList<int> listingIds, bool lockRow, CancellationToken cancellationToken)
    {
        // Locked while placing an order so two orders can't both take the coupon's last use.
        var coupon = await connection.QuerySingleOrDefaultAsync<CouponInfo>(new CommandDefinition($"""
            SELECT id AS Id, code AS Code, discount_type AS DiscountType, discount_value AS DiscountValue,
                   max_discount_amount AS MaxDiscountAmount, min_order_amount AS MinOrderAmount, applies_to AS AppliesTo,
                   first_order_only AS FirstOrderOnly, usage_limit AS UsageLimit,
                   usage_limit_per_customer AS UsageLimitPerCustomer, starts_at AS StartsAt, ends_at AS EndsAt
            FROM public.coupon
            WHERE upper(code) = upper(@Code) AND is_active
            {(lockRow ? "FOR UPDATE" : "")}
            """, new { Code = code }, transaction, cancellationToken: cancellationToken));
        if (coupon is null)
            return null;

        using var results = await connection.QueryMultipleAsync(new CommandDefinition("""
            SELECT count(*)::int FROM public.ecom_order o
            WHERE o.coupon_id = @CouponId AND o.status <> 'cancelled';

            SELECT CASE WHEN CAST(@CustomerId AS integer) IS NULL THEN NULL ELSE count(*)::int END
            FROM public.ecom_order o
            WHERE o.coupon_id = @CouponId AND o.customer_id = CAST(@CustomerId AS integer) AND o.status <> 'cancelled';

            SELECT CASE WHEN CAST(@CustomerId AS integer) IS NULL THEN NULL ELSE count(*)::int END
            FROM public.ecom_order o
            WHERE o.customer_id = CAST(@CustomerId AS integer) AND o.status <> 'cancelled';

            WITH RECURSIVE cats AS (
                SELECT cc.category_id FROM public.coupon_category cc WHERE cc.coupon_id = @CouponId
                UNION
                SELECT c.category_id FROM public.category c
                JOIN cats ON c.parent_category_id = cats.category_id
                WHERE c.organization_id = @Org)
            SELECT l.product_id
            FROM public.product l
            WHERE l.organization_id = @Org
              AND l.product_id = ANY(@ListingIds)
              AND (@AppliesTo = 'all'
                   OR (@AppliesTo = 'products' AND EXISTS (
                           SELECT 1 FROM public.coupon_product cp
                           WHERE cp.coupon_id = @CouponId AND cp.product_id = l.product_id))
                   OR (@AppliesTo = 'categories' AND (
                           l.category_id IN (SELECT category_id FROM cats)
                           OR EXISTS (SELECT 1 FROM public.product_category pc
                                      WHERE pc.product_id = l.product_id
                                        AND pc.category_id IN (SELECT category_id FROM cats)))));
            """, new
        {
            CouponId = coupon.Id,
            CustomerId = customerId,
            Org = organizationId,
            ListingIds = listingIds.ToArray(),
            coupon.AppliesTo
        }, transaction, cancellationToken: cancellationToken));

        coupon.TimesUsed = await results.ReadSingleAsync<int>();
        coupon.TimesUsedByCustomer = await results.ReadSingleAsync<int?>();
        coupon.CustomerOrderCount = await results.ReadSingleAsync<int?>();
        coupon.EligibleListingIds = (await results.ReadAsync<int>()).AsList();
        return coupon;
    }

    private sealed class CustomerStateRow
    {
        public bool IsActive { get; set; }
        public bool IsBlocked { get; set; }
        public string? Email { get; set; }
    }

    private sealed class OrderPlacedRow
    {
        public long Id { get; set; }
        public Guid ObjectId { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string PaymentMethodCode { get; set; } = string.Empty;
        public decimal GrandTotal { get; set; }
    }
}
