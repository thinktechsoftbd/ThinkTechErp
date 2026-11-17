using System.Text;
using Dapper;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.ThinkCart;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.ThinkCart;

public sealed class ThinkCartCatalogRepository(IDbConnectionFactory connectionFactory) : IThinkCartCatalogRepository
{
    private const string BannerColumns = """
        SELECT id AS BannerId, title AS Title, image_url AS ImageUrl, mobile_image_url AS MobileImageUrl,
               link_url AS LinkUrl, placement AS Placement
        FROM public.banner
        """;

    private const string CollectionColumns = """
        SELECT id AS CollectionId, slug AS Slug, name AS Name, description AS Description, image_url AS ImageUrl,
               collection_type AS CollectionType, rules::text AS Rules
        FROM public.collection
        """;

    public async Task<IReadOnlyDictionary<string, string>> GetSiteSettingsAsync(
        IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<(string Key, string Value)>(new CommandDefinition("""
            SELECT key, value::text FROM public.site_setting WHERE key = ANY(@Keys)
            """, new { Keys = keys.ToArray() }, cancellationToken: cancellationToken));
        return rows.ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
    }

    public async Task<string?> GetOrganizationNameAsync(int organizationId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition("""
            SELECT organization_name_en FROM public.organization WHERE organization_id = @Org
            """, new { Org = organizationId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<Banner>> GetBannersAsync(
        IReadOnlyList<string> placements, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<Banner>(new CommandDefinition($"""
            {BannerColumns}
            WHERE placement = ANY(@Placements)
              AND is_active
              AND (starts_at IS NULL OR starts_at <= now())
              AND (ends_at IS NULL OR ends_at > now())
            ORDER BY sort_order, id
            """, new { Placements = placements.ToArray() }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(
        int organizationId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<CatalogCategory>(new CommandDefinition("""
            SELECT category_id AS CategoryId, parent_category_id AS ParentId, slug AS Slug, category_name AS Name,
                   category_name_local AS NameLocal, image_url AS ImageUrl, icon_url AS IconUrl, is_featured AS IsFeatured
            FROM public.category
            WHERE organization_id = @Org AND is_active AND slug IS NOT NULL
            ORDER BY sort_order, category_name, category_id
            """, new { Org = organizationId }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<CatalogBrand>> GetBrandsAsync(
        int organizationId, bool featuredOnly, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<CatalogBrand>(new CommandDefinition("""
            SELECT brand_id AS BrandId, slug AS Slug, brand_name AS Name, logo_url AS LogoUrl, is_featured AS IsFeatured
            FROM public.brand
            WHERE organization_id = @Org AND is_active AND slug IS NOT NULL
              AND (NOT @FeaturedOnly OR is_featured)
            ORDER BY sort_order, brand_name, brand_id
            """, new { Org = organizationId, FeaturedOnly = featuredOnly }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<Testimonial>> GetTestimonialsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<Testimonial>(new CommandDefinition("""
            SELECT name AS Name, designation AS Designation, message AS Message, photo_url AS PhotoUrl
            FROM public.ecom_testimonial
            WHERE is_active
            ORDER BY sort_order, id
            """, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<CollectionRecord?> GetCollectionAsync(string slug, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<CollectionRecord>(new CommandDefinition($"""
            {CollectionColumns}
            WHERE lower(slug) = lower(@Slug) AND is_active
            LIMIT 1
            """, new { Slug = slug }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<CollectionRecord>> GetHomeCollectionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<CollectionRecord>(new CommandDefinition($"""
            {CollectionColumns}
            WHERE show_on_home AND is_active
            ORDER BY sort_order, id
            """, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<int>> GetCollectionProductIdsAsync(
        int collectionId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<int>(new CommandDefinition("""
            SELECT product_id FROM public.collection_product
            WHERE collection_id = @CollectionId
            ORDER BY sort_order, product_id
            """, new { CollectionId = collectionId }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<PagedResult<ProductCard>> GetProductCardsAsync(
        int organizationId, ProductCardQuery query, bool includeTotal, CancellationToken cancellationToken = default)
    {
        var (cte, orderBy) = BuildCardQuery(query);
        var parameters = new
        {
            Org = organizationId,
            Pattern = query.Search is null ? null : SecurityDb.ContainsPattern(query.Search),
            Prefix = query.Search is null ? null : SecurityDb.ContainsPattern(query.Search)[1..],
            query.CategorySlug,
            query.CategoryId,
            query.BrandSlug,
            query.BrandId,
            query.TagSlug,
            query.MinPrice,
            query.MaxPrice,
            Ids = query.Ids?.ToArray(),
            ExcludeIds = query.ExcludeIds?.ToArray(),
            query.SalesSinceDays,
            query.Limit,
            query.Offset
        };

        var rowsSql = $"""
            {cte}
            SELECT f.listing_id AS ProductId,
                   f.slug AS Slug,
                   f.product_name AS Name,
                   f.product_name_local AS NameLocal,
                   coalesce(
                       (SELECT pi.url FROM public.product_image pi
                        WHERE pi.product_id = f.listing_id
                        ORDER BY pi.is_primary DESC, (pi.product_variant_id IS NULL) DESC, pi.sort_order, pi.id
                        LIMIT 1),
                       f.listing_image_url,
                       f.size_image_url) AS ImageUrl,
                   f.brand_name AS BrandName,
                   f.variant_id AS DefaultVariantId,
                   f.title AS VariantTitle,
                   f.variant_count::int AS VariantCount,
                   f.price AS Price,
                   f.compare_raw AS CompareAtRaw,
                   f.in_stock AS InStock,
                   r.average AS AverageRating,
                   r.review_count AS ReviewCount
            FROM f
            LEFT JOIN LATERAL (
                SELECT round(avg(pr.rating)::numeric, 1) AS average, count(*)::int AS review_count
                FROM public.product_review pr
                WHERE pr.product_id = f.listing_id AND pr.status = 'approved') r ON true
            ORDER BY {orderBy}
            LIMIT @Limit OFFSET @Offset
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var total = includeTotal
            ? (int)await connection.QuerySingleAsync<long>(new CommandDefinition(
                $"{cte} SELECT count(*) FROM f", parameters, cancellationToken: cancellationToken))
            : 0;
        var rows = (await connection.QueryAsync<CardRow>(new CommandDefinition(
            rowsSql, parameters, cancellationToken: cancellationToken))).AsList();

        var badges = await GetBadgesAsync(connection, rows.Select(row => row.ProductId).ToArray(), cancellationToken);
        var cards = rows.Select(row =>
        {
            var compareAt = ThinkCartPricing.CompareAt(row.Price, row.CompareAtRaw);
            return new ProductCard
            {
                ProductId = row.ProductId,
                Slug = row.Slug,
                Name = row.Name,
                NameLocal = row.NameLocal,
                ImageUrl = row.ImageUrl,
                BrandName = row.BrandName,
                DefaultVariantId = row.DefaultVariantId,
                VariantTitle = row.VariantTitle,
                VariantCount = row.VariantCount,
                Price = row.Price,
                CompareAtPrice = compareAt,
                DiscountPercent = ThinkCartPricing.DiscountPercent(row.Price, compareAt),
                InStock = row.InStock,
                Badges = badges.TryGetValue(row.ProductId, out var list) ? list : [],
                AverageRating = row.AverageRating,
                ReviewCount = row.ReviewCount
            };
        }).ToList();

        return new PagedResult<ProductCard>
        {
            Items = cards,
            TotalCount = includeTotal ? total : cards.Count,
            Limit = query.Limit,
            Offset = query.Offset
        };
    }

    public async Task<ProductDetailData?> GetProductDetailAsync(
        int organizationId, string slug, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var header = await connection.QuerySingleOrDefaultAsync<DetailHeader>(new CommandDefinition("""
            SELECT l.product_id AS ProductId, l.object_id AS ObjectId, l.slug AS Slug, l.product_name AS Name,
                   l.product_name_local AS NameLocal, l.short_description AS ShortDescription,
                   l.description AS Description, l.video_url AS VideoUrl, trim(l.country_of_origin) AS CountryOfOrigin,
                   l.meta_title AS MetaTitle, l.meta_description AS MetaDescription,
                   l.category_id AS CategoryId, l.brand_id AS BrandId, l.image_url AS ImageUrl
            FROM public.product l
            WHERE l.organization_id = @Org AND lower(l.slug) = lower(@Slug)
              AND l.web_status = 'active' AND l.is_active
            ORDER BY l.product_id
            LIMIT 1
            """, new { Org = organizationId, Slug = slug }, cancellationToken: cancellationToken));
        if (header is null)
            return null;

        var parameters = new { Org = organizationId, header.ProductId, header.CategoryId, header.BrandId };
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT {ThinkCartSql.VariantLineColumns}
            FROM public.product_variant pv
            {ThinkCartSql.VariantJoins}
            WHERE l.product_id = @ProductId AND {ThinkCartSql.Visible}
            ORDER BY pv.sort_order, pv.id;

            SELECT pi.url AS Url, pi.alt_text AS AltText, pi.product_variant_id AS VariantId
            FROM public.product_image pi
            WHERE pi.product_id = @ProductId
            ORDER BY pi.is_primary DESC, pi.sort_order, pi.id;

            SELECT t.name AS Name, t.slug AS Slug, t.color AS Color
            FROM public.product_tag pt
            JOIN public.tag t ON t.id = pt.tag_id
            WHERE pt.product_id = @ProductId AND t.tag_type = 'badge'
            ORDER BY t.name, t.id;

            SELECT rating::int AS Rating, count(*)::int AS Count
            FROM public.product_review
            WHERE product_id = @ProductId AND status = 'approved'
            GROUP BY rating;

            SELECT count(*)::int
            FROM public.product_review
            WHERE product_id = @ProductId AND status = 'approved' AND is_recommended;

            WITH RECURSIVE up AS (
                SELECT c.category_id, c.parent_category_id, c.slug, c.category_name, 0 AS depth
                FROM public.category c
                WHERE c.category_id = @CategoryId AND c.organization_id = @Org
                UNION ALL
                SELECT c.category_id, c.parent_category_id, c.slug, c.category_name, up.depth + 1
                FROM public.category c
                JOIN up ON c.category_id = up.parent_category_id
                WHERE c.organization_id = @Org AND up.depth < 20)
            SELECT category_id AS CategoryId, coalesce(slug, '') AS Slug, category_name AS Name
            FROM up
            ORDER BY depth DESC;

            SELECT brand_id AS BrandId, coalesce(slug, '') AS Slug, brand_name AS Name, logo_url AS LogoUrl,
                   is_featured AS IsFeatured
            FROM public.brand
            WHERE brand_id = @BrandId AND organization_id = @Org AND is_active;

            SELECT related_product_id
            FROM public.product_relation
            WHERE product_id = @ProductId AND relation_type = 'frequently_bought_together'
            ORDER BY sort_order, related_product_id;
            """, parameters, cancellationToken: cancellationToken));

        var lines = (await results.ReadAsync<VariantLine>()).AsList();
        var images = (await results.ReadAsync<ProductImageView>()).AsList();
        var badges = (await results.ReadAsync<Badge>()).AsList();
        var stars = (await results.ReadAsync<RatingStar>()).ToDictionary(star => star.Rating, star => star.Count);
        var recommended = await results.ReadSingleAsync<int>();
        var path = (await results.ReadAsync<CategoryRef>()).AsList();
        var brand = await results.ReadSingleOrDefaultAsync<CatalogBrand>();
        var fbtIds = (await results.ReadAsync<int>()).AsList();

        if (lines.Count == 0)
            return null;

        // As on the cards: the size flagged default, else the first in sort order.
        var defaultVariantId = (lines.FirstOrDefault(line => line.IsDefault) ?? lines[0]).VariantId;

        if (images.Count == 0)
        {
            // No gallery yet: fall back to the ERP pictures of the listing and its sizes.
            if (!string.IsNullOrWhiteSpace(header.ImageUrl))
                images.Add(new ProductImageView { Url = header.ImageUrl, AltText = header.Name });
            images.AddRange(lines
                .Where(line => !string.IsNullOrWhiteSpace(line.ImageUrl) && line.ImageUrl != header.ImageUrl)
                .Select(line => new ProductImageView { Url = line.ImageUrl!, AltText = line.Title, VariantId = line.VariantId }));
        }

        var reviewCount = stars.Values.Sum();
        var detail = new ProductDetail
        {
            ProductId = header.ProductId,
            ObjectId = header.ObjectId,
            Slug = header.Slug,
            Name = header.Name,
            NameLocal = header.NameLocal,
            ShortDescription = header.ShortDescription,
            Description = header.Description,
            VideoUrl = header.VideoUrl,
            CountryOfOrigin = string.IsNullOrWhiteSpace(header.CountryOfOrigin) ? null : header.CountryOfOrigin,
            MetaTitle = header.MetaTitle,
            MetaDescription = header.MetaDescription,
            Brand = brand,
            Category = path.Count == 0 ? null : path[^1],
            Breadcrumbs = path.Select(step => new Breadcrumb { Name = step.Name, Slug = step.Slug }).ToList(),
            Images = images,
            Variants = lines.Select(line =>
            {
                var compareAt = ThinkCartPricing.CompareAt(line.Price, line.CompareAtRaw);
                return new ProductVariantView
                {
                    VariantId = line.VariantId,
                    ProductId = line.SizeProductId,
                    Sku = line.Sku,
                    Title = line.Title,
                    PackSize = line.PackSize is decimal packSize ? ThinkCartPricing.Quantity(packSize) : null,
                    PackUnit = line.PackUnit,
                    WeightG = line.WeightG,
                    Price = line.Price,
                    CompareAtPrice = compareAt,
                    DiscountPercent = ThinkCartPricing.DiscountPercent(line.Price, compareAt),
                    InStock = line.InStock,
                    AvailableQuantity = ThinkCartPricing.Quantity(Math.Max(line.AvailableQuantity, 0)),
                    MinOrderQty = line.MinOrderQty,
                    MaxOrderQty = line.MaxOrderQty,
                    IsDefault = line.VariantId == defaultVariantId,
                    ImageUrl = line.ImageUrl
                };
            }).ToList(),
            Badges = badges,
            Rating = new ProductRating
            {
                Count = reviewCount,
                Average = reviewCount == 0
                    ? null
                    : Math.Round(stars.Sum(star => (decimal)star.Key * star.Value) / reviewCount, 1, MidpointRounding.AwayFromZero),
                RecommendedCount = recommended,
                Stars = Enumerable.Range(1, 5).Reverse()
                    .Select(rating => new RatingStar { Rating = rating, Count = stars.GetValueOrDefault(rating) })
                    .ToList()
            }
        };
        return new ProductDetailData(detail, fbtIds);
    }

    public async Task<ListingRef?> GetVisibleListingAsync(
        int organizationId, string slug, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<ListingRow>(new CommandDefinition($"""
            SELECT l.product_id AS ListingId, l.category_id AS CategoryId
            FROM public.product l
            WHERE l.organization_id = @Org AND lower(l.slug) = lower(@Slug)
              AND l.web_status = 'active' AND l.is_active
              AND EXISTS (
                  SELECT 1
                  FROM public.product p
                  JOIN public.product_variant pv ON pv.product_id = p.product_id
                  WHERE coalesce(p.parent_product_id, p.product_id) = l.product_id
                    AND p.organization_id = @Org
                    AND {ThinkCartSql.Visible})
            ORDER BY l.product_id
            LIMIT 1
            """, new { Org = organizationId, Slug = slug }, cancellationToken: cancellationToken));
        return row is null ? null : new ListingRef(row.ListingId, row.CategoryId);
    }

    public async Task<IReadOnlyList<int>> GetRelatedListingIdsAsync(
        IReadOnlyList<int> listingIds, IReadOnlyList<string> relationTypes, CancellationToken cancellationToken = default)
    {
        if (listingIds.Count == 0 || relationTypes.Count == 0)
            return [];

        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<int>(new CommandDefinition("""
            SELECT pr.related_product_id
            FROM public.product_relation pr
            WHERE pr.product_id = ANY(@Ids)
              AND pr.relation_type = ANY(@Types)
              AND pr.related_product_id <> ALL(@Ids)
            GROUP BY pr.related_product_id
            ORDER BY min(array_position(CAST(@Types AS text[]), pr.relation_type)), min(pr.sort_order), pr.related_product_id
            LIMIT 200
            """, new { Ids = listingIds.ToArray(), Types = relationTypes.ToArray() },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<PagedResult<Review>> GetReviewsAsync(
        int listingId, int limit, int offset, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        using var results = await connection.QueryMultipleAsync(new CommandDefinition("""
            SELECT count(*) FROM public.product_review WHERE product_id = @ListingId AND status = 'approved';

            SELECT r.id AS Id, r.object_id AS ReviewId, r.reviewer_name AS ReviewerName, r.rating::int AS Rating,
                   r.is_recommended AS IsRecommended, r.comment AS Comment,
                   (r.order_id IS NOT NULL) AS IsVerifiedPurchase, r.created_at AS CreatedAt
            FROM public.product_review r
            WHERE r.product_id = @ListingId AND r.status = 'approved'
            ORDER BY r.created_at DESC, r.id DESC
            LIMIT @Limit OFFSET @Offset;
            """, new { ListingId = listingId, Limit = limit, Offset = offset }, cancellationToken: cancellationToken));
        var total = await results.ReadSingleAsync<long>();
        var rows = (await results.ReadAsync<ReviewRow>()).AsList();

        List<(long ReviewId, string Url)> images = rows.Count == 0
            ? []
            : (await connection.QueryAsync<(long ReviewId, string Url)>(new CommandDefinition("""
                SELECT review_id, url FROM public.review_image
                WHERE review_id = ANY(@Ids)
                ORDER BY review_id, sort_order, id
                """, new { Ids = rows.Select(row => row.Id).ToArray() }, cancellationToken: cancellationToken))).AsList();

        var reviews = rows.Select(row => new Review
        {
            ReviewId = row.ReviewId,
            ReviewerName = row.ReviewerName,
            Rating = row.Rating,
            IsRecommended = row.IsRecommended,
            Comment = row.Comment,
            Images = images.Where(image => image.ReviewId == row.Id).Select(image => image.Url).ToList(),
            IsVerifiedPurchase = row.IsVerifiedPurchase,
            CreatedAt = row.CreatedAt
        }).ToList();
        return new PagedResult<Review> { Items = reviews, TotalCount = (int)total, Limit = limit, Offset = offset };
    }

    public async Task<long?> FindDeliveredOrderAsync(int customerId, int listingId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition("""
            SELECT o.id
            FROM public.ecom_order o
            JOIN public.ecom_order_item oi ON oi.order_id = o.id
            JOIN public.product sp ON sp.product_id = oi.product_id
            WHERE o.customer_id = @CustomerId
              AND o.status = 'delivered'
              AND coalesce(sp.parent_product_id, sp.product_id) = @ListingId
            ORDER BY o.delivered_at DESC NULLS LAST, o.id DESC
            LIMIT 1
            """, new { CustomerId = customerId, ListingId = listingId }, cancellationToken: cancellationToken));
    }

    public async Task<Review> CreateReviewAsync(ReviewDraft draft, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var created = await connection.QuerySingleAsync<(long Id, Guid ObjectId, DateTime CreatedAt)>(new CommandDefinition("""
            INSERT INTO public.product_review
                (product_id, customer_id, order_id, reviewer_name, reviewer_email, rating, is_recommended, comment, status)
            VALUES
                (@ListingId, @CustomerId, @OrderId, @ReviewerName, @ReviewerEmail, @Rating, @IsRecommended, @Comment, 'pending')
            RETURNING id, object_id, created_at
            """, new
        {
            draft.ListingId,
            draft.CustomerId,
            draft.OrderId,
            draft.ReviewerName,
            draft.ReviewerEmail,
            Rating = (short)draft.Rating,
            draft.IsRecommended,
            draft.Comment
        }, transaction, cancellationToken: cancellationToken));

        if (draft.ImageUrls.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO public.review_image (review_id, url, sort_order)
                SELECT @ReviewId, image.url, (image.position - 1)::smallint
                FROM unnest(CAST(@Urls AS text[])) WITH ORDINALITY AS image(url, position)
                """, new { ReviewId = created.Id, Urls = draft.ImageUrls.ToArray() }, transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return new Review
        {
            ReviewId = created.ObjectId,
            ReviewerName = draft.ReviewerName,
            Rating = draft.Rating,
            IsRecommended = draft.IsRecommended,
            Comment = draft.Comment,
            Images = draft.ImageUrls,
            IsVerifiedPurchase = draft.OrderId is not null,
            CreatedAt = created.CreatedAt
        };
    }

    public async Task<StorefrontPage?> GetPageAsync(string slug, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<StorefrontPage>(new CommandDefinition("""
            SELECT slug AS Slug, title AS Title, content AS Content, meta_title AS MetaTitle,
                   meta_description AS MetaDescription
            FROM public.ecom_page
            WHERE lower(slug) = lower(@Slug) AND is_published
            LIMIT 1
            """, new { Slug = slug }, cancellationToken: cancellationToken));
    }

    public async Task AddEventsAsync(
        Guid? sessionId, int? customerId, IReadOnlyList<AnalyticsEventInput> events, DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (events.Count == 0)
            return;

        await using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO public.analytics_event
                (session_id, customer_id, event_name, product_id, product_variant_id, category_id, order_id,
                 search_query, quantity, value, page_url, occurred_at)
            SELECT @SessionId, @CustomerId, e.event_name, e.product_id, e.variant_id, e.category_id,
                   (SELECT o.id FROM public.ecom_order o WHERE o.order_number = e.order_number),
                   e.search_query, e.quantity, e.value, e.page_url, e.occurred_at
            FROM unnest(CAST(@Names AS text[]), CAST(@ProductIds AS integer[]), CAST(@VariantIds AS bigint[]),
                        CAST(@CategoryIds AS integer[]), CAST(@OrderNumbers AS text[]), CAST(@SearchQueries AS text[]),
                        CAST(@Quantities AS integer[]), CAST(@Values AS numeric[]), CAST(@PageUrls AS text[]),
                        CAST(@OccurredAt AS timestamptz[]))
                 AS e(event_name, product_id, variant_id, category_id, order_number, search_query,
                      quantity, value, page_url, occurred_at)
            """, new
        {
            SessionId = sessionId,
            CustomerId = customerId,
            Names = events.Select(e => e.EventName).ToArray(),
            ProductIds = events.Select(e => e.ProductId).ToArray(),
            VariantIds = events.Select(e => e.VariantId).ToArray(),
            CategoryIds = events.Select(e => e.CategoryId).ToArray(),
            OrderNumbers = events.Select(e => e.OrderNumber).ToArray(),
            SearchQueries = events.Select(e => e.SearchQuery).ToArray(),
            Quantities = events.Select(e => e.Quantity).ToArray(),
            Values = events.Select(e => e.Value).ToArray(),
            PageUrls = events.Select(e => e.PageUrl).ToArray(),
            OccurredAt = events.Select(e => e.OccurredAt?.UtcDateTime ?? nowUtc).ToArray()
        }, cancellationToken: cancellationToken));
    }

    private static async Task<Dictionary<int, List<Badge>>> GetBadgesAsync(
        Npgsql.NpgsqlConnection connection, int[] listingIds, CancellationToken cancellationToken)
    {
        if (listingIds.Length == 0)
            return new Dictionary<int, List<Badge>>();

        var rows = await connection.QueryAsync<(int ProductId, string Name, string Slug, string? Color)>(new CommandDefinition("""
            SELECT pt.product_id, t.name, t.slug, t.color
            FROM public.product_tag pt
            JOIN public.tag t ON t.id = pt.tag_id
            WHERE pt.product_id = ANY(@Ids) AND t.tag_type = 'badge'
            ORDER BY t.name, t.id
            """, new { Ids = listingIds }, cancellationToken: cancellationToken));
        return rows
            .GroupBy(row => row.ProductId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => new Badge { Name = row.Name, Slug = row.Slug, Color = row.Color }).ToList());
    }

    /// <summary>
    /// The CTE (ending in f: one row per visible listing with its default size priced now, after the
    /// filters) and the ORDER BY for a card list.
    /// </summary>
    private static (string Cte, string OrderBy) BuildCardQuery(ProductCardQuery query)
    {
        var ctes = new List<string>();
        var listingFilters = new StringBuilder();
        var cardFilters = new StringBuilder();
        var withSales = query.Sort == ProductSorts.BestSelling || query.SoldOnly;

        if (query.CategorySlug is not null || query.CategoryId is not null)
        {
            // The category and its active sub-categories, for the reporting category and product_category.
            ctes.Add("""
                cat AS (
                    SELECT c.category_id
                    FROM public.category c
                    WHERE c.organization_id = @Org AND c.is_active
                      AND ((CAST(@CategorySlug AS text) IS NOT NULL AND lower(c.slug) = lower(@CategorySlug))
                           OR c.category_id = CAST(@CategoryId AS integer))
                    UNION
                    SELECT c.category_id
                    FROM public.category c
                    JOIN cat ON c.parent_category_id = cat.category_id
                    WHERE c.organization_id = @Org AND c.is_active)
                """);
            listingFilters.Append("""

                AND (l.category_id IN (SELECT category_id FROM cat)
                     OR EXISTS (SELECT 1 FROM public.product_category pc
                                WHERE pc.product_id = l.product_id AND pc.category_id IN (SELECT category_id FROM cat)))
                """);
        }

        if (withSales)
        {
            ctes.Add($"""
                sales AS (
                    SELECT coalesce(sp.parent_product_id, sp.product_id) AS listing_id, sum(oi.quantity) AS units
                    FROM public.ecom_order_item oi
                    JOIN public.ecom_order o ON o.id = oi.order_id
                    JOIN public.product sp ON sp.product_id = oi.product_id AND sp.organization_id = @Org
                    WHERE {ThinkCartSql.BookedOrder}
                      AND (CAST(@SalesSinceDays AS integer) IS NULL
                           OR o.placed_at >= now() - make_interval(days => CAST(@SalesSinceDays AS integer)))
                    GROUP BY 1)
                """);
        }

        if (query.Search is not null)
            listingFilters.Append("""

                AND (l.product_name ILIKE @Pattern OR l.product_name_local ILIKE @Pattern OR b.brand_name ILIKE @Pattern)
                """);
        if (query.BrandSlug is not null)
            listingFilters.Append("\nAND lower(b.slug) = lower(@BrandSlug)");
        if (query.BrandId is not null)
            listingFilters.Append("\nAND l.brand_id = @BrandId AND b.brand_id IS NOT NULL");
        if (query.TagSlug is not null)
            listingFilters.Append("""

                AND EXISTS (SELECT 1 FROM public.product_tag pt JOIN public.tag t ON t.id = pt.tag_id
                            WHERE pt.product_id = l.product_id AND lower(t.slug) = lower(@TagSlug))
                """);
        if (query.FeaturedOnly)
            listingFilters.Append("\nAND l.is_featured");
        if (query.Ids is not null)
            listingFilters.Append("\nAND l.product_id = ANY(@Ids)");
        if (query.ExcludeIds is { Count: > 0 })
            listingFilters.Append("\nAND l.product_id <> ALL(@ExcludeIds)");

        if (query.MinPrice is not null)
            cardFilters.Append("\nAND d.price >= @MinPrice");
        if (query.MaxPrice is not null)
            cardFilters.Append("\nAND d.price <= @MaxPrice");
        if (query.SoldOnly)
            cardFilters.Append("\nAND coalesce(s.units, 0) > 0");

        ctes.Add($"""
            v AS (
                SELECT pv.id AS variant_id,
                       l.product_id AS listing_id,
                       pv.title,
                       pv.is_default,
                       pv.sort_order AS variant_sort,
                       coalesce(cp.sale_price, round(p.sale_price, 2)) AS price,
                       coalesce(pv.compare_at_price, round(p.mrp, 2)) AS compare_raw,
                       (coalesce(st.available, 0) > 0 OR pv.allow_backorder OR p.product_type <> 'Stock') AS in_stock,
                       p.image_url AS size_image_url,
                       l.slug,
                       l.product_name,
                       l.product_name_local,
                       l.image_url AS listing_image_url,
                       l.is_featured,
                       l.sort_order AS listing_sort,
                       coalesce(l.published_at, l.created_at) AS listed_at,
                       b.brand_name
                FROM public.product_variant pv
                {ThinkCartSql.VariantJoins}
                LEFT JOIN public.brand b ON b.brand_id = l.brand_id AND b.organization_id = @Org
                WHERE {ThinkCartSql.Visible}{listingFilters})
            """);
        ctes.Add("""
            d AS (
                SELECT DISTINCT ON (v.listing_id) v.*, count(*) OVER (PARTITION BY v.listing_id) AS variant_count
                FROM v
                ORDER BY v.listing_id, v.is_default DESC, v.variant_sort, v.variant_id)
            """);
        ctes.Add($"""
            f AS (
                SELECT d.*{(withSales ? ", coalesce(s.units, 0) AS units" : "")}
                FROM d
                {(withSales ? "LEFT JOIN sales s ON s.listing_id = d.listing_id" : "")}
                WHERE true{cardFilters})
            """);

        var orderBy = query.Sort switch
        {
            ProductSorts.Newest => "f.listed_at DESC, f.listing_id DESC",
            ProductSorts.PriceAsc => "f.price, f.listing_id",
            ProductSorts.PriceDesc => "f.price DESC, f.listing_id",
            ProductSorts.BestSelling => "f.units DESC, f.is_featured DESC, f.listing_sort, f.listing_id DESC",
            ProductSorts.Name => "lower(f.product_name), f.listing_id",
            ProductSorts.IdOrder when query.Ids is not null =>
                "array_position(CAST(@Ids AS integer[]), f.listing_id), f.listing_id",
            ProductSorts.Suggest =>
                "(f.product_name ILIKE @Prefix) DESC, f.is_featured DESC, lower(f.product_name), f.listing_id",
            _ => "f.is_featured DESC, f.listing_sort, f.listed_at DESC, f.listing_id DESC"
        };

        var recursive = query.CategorySlug is not null || query.CategoryId is not null ? "RECURSIVE " : "";
        return ($"WITH {recursive}{string.Join(",\n", ctes)}", orderBy);
    }

    private sealed class CardRow
    {
        public int ProductId { get; set; }
        public string Slug { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? NameLocal { get; set; }
        public string? ImageUrl { get; set; }
        public string? BrandName { get; set; }
        public long DefaultVariantId { get; set; }
        public string VariantTitle { get; set; } = string.Empty;
        public int VariantCount { get; set; }
        public decimal Price { get; set; }
        public decimal? CompareAtRaw { get; set; }
        public bool InStock { get; set; }
        public decimal? AverageRating { get; set; }
        public int ReviewCount { get; set; }
    }

    private sealed class DetailHeader
    {
        public int ProductId { get; set; }
        public Guid ObjectId { get; set; }
        public string Slug { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? NameLocal { get; set; }
        public string? ShortDescription { get; set; }
        public string? Description { get; set; }
        public string? VideoUrl { get; set; }
        public string? CountryOfOrigin { get; set; }
        public string? MetaTitle { get; set; }
        public string? MetaDescription { get; set; }
        public int? CategoryId { get; set; }
        public int? BrandId { get; set; }
        public string? ImageUrl { get; set; }
    }

    private sealed class ListingRow
    {
        public int ListingId { get; set; }
        public int? CategoryId { get; set; }
    }

    private sealed class ReviewRow
    {
        public long Id { get; set; }
        public Guid ReviewId { get; set; }
        public string ReviewerName { get; set; } = string.Empty;
        public int Rating { get; set; }
        public bool? IsRecommended { get; set; }
        public string? Comment { get; set; }
        public bool IsVerifiedPurchase { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
