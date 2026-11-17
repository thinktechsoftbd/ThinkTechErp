namespace ThinkTechErpRepository.ThinkCart;

/// <summary>
/// SQL shared by the ThinkCart repositories. Aliases: pv = product_variant (web settings of one size),
/// p = the size's ERP product, l = its listing product (COALESCE(p.parent_product_id, p.product_id)).
/// Every fragment filters the ERP tables by @Org; the e-commerce tables carry no organization.
/// </summary>
internal static class ThinkCartSql
{
    /// <summary>
    /// cp: the live campaign price of the size, the lowest of the active campaigns running now whose
    /// stock limit (units sold at that price on orders not cancelled) isn't reached.
    /// </summary>
    public const string CampaignLateral = """
        LEFT JOIN LATERAL (
            SELECT ci.sale_price, ci.campaign_id, ci.max_qty_per_order
            FROM public.campaign_item ci
            JOIN public.campaign c ON c.id = ci.campaign_id
            WHERE ci.product_variant_id = pv.id
              AND c.is_active
              AND now() BETWEEN c.starts_at AND c.ends_at
              AND (ci.stock_limit IS NULL OR ci.stock_limit > (
                    SELECT coalesce(sum(oi.quantity), 0)
                    FROM public.ecom_order_item oi
                    JOIN public.ecom_order o ON o.id = oi.order_id
                    WHERE oi.campaign_id = ci.campaign_id
                      AND oi.product_variant_id = ci.product_variant_id
                      AND o.status <> 'cancelled'))
            ORDER BY ci.sale_price, c.id
            LIMIT 1) cp ON true
        """;

    /// <summary>st: Σ available_quantity of the size in the organization, and its average cost.</summary>
    public const string StockLateral = """
        LEFT JOIN LATERAL (
            SELECT sum(sb.available_quantity) AS available,
                   sum(sb.quantity * sb.average_cost) FILTER (WHERE sb.quantity > 0)
                       / nullif(sum(sb.quantity) FILTER (WHERE sb.quantity > 0), 0) AS average_cost
            FROM public.stock_balance sb
            WHERE sb.organization_id = @Org AND sb.product_id = p.product_id) st ON true
        """;

    /// <summary>Joins after "pv": the size product, its listing, the live campaign price and the stock.</summary>
    public const string VariantJoins = $"""
        JOIN public.product p ON p.product_id = pv.product_id AND p.organization_id = @Org
        JOIN public.product l ON l.product_id = coalesce(p.parent_product_id, p.product_id) AND l.organization_id = @Org
        {CampaignLateral}
        {StockLateral}
        """;

    /// <summary>On sale on the website: an active web listing with a slug, an active size and an active variant.</summary>
    public const string Visible =
        "(pv.status = 'active' AND p.is_active AND l.web_status = 'active' AND l.is_active AND l.slug IS NOT NULL)";

    /// <summary>The listing's main picture: primary first, then pictures not tied to one size.</summary>
    public const string ListingImage = """
        (SELECT pi.url FROM public.product_image pi
         WHERE pi.product_id = l.product_id
         ORDER BY pi.is_primary DESC, (pi.product_variant_id IS NULL) DESC, pi.sort_order, pi.id
         LIMIT 1)
        """;

    /// <summary>VariantLine columns (needs VariantJoins).</summary>
    public const string VariantLineColumns = $"""
        pv.id AS VariantId,
        p.product_id AS SizeProductId,
        l.product_id AS ListingId,
        l.slug AS Slug,
        l.product_name AS Name,
        pv.title AS Title,
        p.product_code AS Sku,
        pv.pack_size AS PackSize,
        pv.pack_unit AS PackUnit,
        pv.weight_g AS WeightG,
        round(p.sale_price, 2) AS ListPrice,
        coalesce(cp.sale_price, round(p.sale_price, 2)) AS Price,
        cp.campaign_id AS CampaignId,
        coalesce(pv.compare_at_price, round(p.mrp, 2)) AS CompareAtRaw,
        coalesce(st.available, 0) AS AvailableQuantity,
        pv.allow_backorder AS AllowBackorder,
        p.product_type AS ProductType,
        pv.min_order_qty AS MinOrderQty,
        least(pv.max_order_qty, cp.max_qty_per_order) AS MaxOrderQty,
        pv.is_default AS IsDefault,
        {Visible} AS IsVisible,
        coalesce(p.category_id, l.category_id) AS CategoryId,
        round(coalesce(st.average_cost, p.last_purchase_cost), 2) AS UnitCost,
        coalesce(
            (SELECT pi.url FROM public.product_image pi
             WHERE pi.product_variant_id = pv.id
             ORDER BY pi.is_primary DESC, pi.sort_order, pi.id LIMIT 1),
            p.image_url,
            {ListingImage},
            l.image_url) AS ImageUrl
        """;

    /// <summary>Orders count as booked unless cancelled.</summary>
    public const string BookedOrder = "o.status <> 'cancelled'";
}
