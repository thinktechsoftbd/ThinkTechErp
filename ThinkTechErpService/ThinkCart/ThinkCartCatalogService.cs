using System.Text.Json;
using System.Text.RegularExpressions;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.ThinkCart;
using ThinkTechErpRepository.ThinkCart;

namespace ThinkTechErpService.ThinkCart;

public interface IThinkCartCatalogService
{
    Task<SiteInfo> GetSiteAsync(int organizationId, CancellationToken cancellationToken = default);
    Task<HomePage> GetHomeAsync(int organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(int organizationId, CancellationToken cancellationToken = default);
    Task<CatalogCategory?> GetCategoryAsync(int organizationId, string slug, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogBrand>> GetBrandsAsync(int organizationId, CancellationToken cancellationToken = default);
    Task<CatalogCollection?> GetCollectionAsync(
        int organizationId, string slug, int limit, int offset, CancellationToken cancellationToken = default);
    Task<PagedResult<ProductCard>> SearchProductsAsync(
        int organizationId, ProductSearchRequest request, CancellationToken cancellationToken = default);
    Task<ProductDetail?> GetProductAsync(int organizationId, string slug, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductCard>?> GetRelatedProductsAsync(
        int organizationId, string slug, int limit, CancellationToken cancellationToken = default);
    Task<PagedResult<Review>?> GetReviewsAsync(
        int organizationId, string slug, int limit, int offset, CancellationToken cancellationToken = default);
    Task<Review?> CreateReviewAsync(
        int organizationId, int? customerId, string slug, ReviewCreateRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductCard>> SuggestAsync(int organizationId, string? query, CancellationToken cancellationToken = default);
    Task<StorefrontPage?> GetPageAsync(string slug, CancellationToken cancellationToken = default);
    Task RecordEventsAsync(
        int? customerId, AnalyticsEventBatch batch, CancellationToken cancellationToken = default);

    /// <summary>Cards for these listings, in this order (invisible ones are left out).</summary>
    Task<IReadOnlyList<ProductCard>> GetCardsAsync(
        int organizationId, IReadOnlyList<int> listingIds, int limit, CancellationToken cancellationToken = default);
}

/// <summary>GET /products filters: category, brand and tag are slugs.</summary>
public sealed class ProductSearchRequest
{
    public string? Search { get; set; }
    public string? Category { get; set; }
    public string? Brand { get; set; }
    public string? Tag { get; set; }
    public string? Sort { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public int Limit { get; set; } = 24;
    public int Offset { get; set; }
}

public sealed partial class ThinkCartCatalogService(IThinkCartCatalogRepository catalog) : IThinkCartCatalogService
{
    private static readonly string[] SiteKeys = [ThinkCartSiteSettings.Store, ThinkCartSiteSettings.Checkout];

    public async Task<SiteInfo> GetSiteAsync(int organizationId, CancellationToken cancellationToken = default)
    {
        var settings = await catalog.GetSiteSettingsAsync(SiteKeys, cancellationToken);
        var organizationName = await catalog.GetOrganizationNameAsync(organizationId, cancellationToken);
        return ThinkCartSiteSettings.BuildSiteInfo(settings, organizationName);
    }

    public async Task<HomePage> GetHomeAsync(int organizationId, CancellationToken cancellationToken = default)
    {
        var banners = await catalog.GetBannersAsync(["home_hero", "home_middle"], cancellationToken);
        var categories = await catalog.GetCategoriesAsync(organizationId, cancellationToken);

        // Best sellers of the last 90 days (booked units); the featured listings until there are sales.
        var topSelling = (await catalog.GetProductCardsAsync(organizationId, new ProductCardQuery
        {
            Sort = ProductSorts.BestSelling,
            SalesSinceDays = ThinkCartRules.TopSellingDays,
            SoldOnly = true,
            Limit = ThinkCartRules.HomeTopSellingLimit
        }, false, cancellationToken)).Items;
        if (topSelling.Count == 0)
        {
            topSelling = (await catalog.GetProductCardsAsync(organizationId, new ProductCardQuery
            {
                FeaturedOnly = true,
                Limit = ThinkCartRules.HomeTopSellingLimit
            }, false, cancellationToken)).Items;
        }

        var collections = new List<CatalogCollection>();
        foreach (var record in await catalog.GetHomeCollectionsAsync(cancellationToken))
            collections.Add(await LoadCollectionAsync(organizationId, record, ThinkCartRules.HomeCollectionLimit, 0, cancellationToken));

        return new HomePage
        {
            HeroBanners = banners.Where(banner => banner.Placement == "home_hero").ToList(),
            SideBanners = banners.Where(banner => banner.Placement == "home_middle").ToList(),
            FeaturedCategories = categories
                .Where(category => category.IsFeatured)
                .Select(category => Copy(category, withChildren: false))
                .ToList(),
            TopSelling = topSelling,
            FeaturedBrands = await catalog.GetBrandsAsync(organizationId, featuredOnly: true, cancellationToken),
            Collections = collections,
            Testimonials = await catalog.GetTestimonialsAsync(cancellationToken)
        };
    }

    public async Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(
        int organizationId, CancellationToken cancellationToken = default)
        => BuildTree(await catalog.GetCategoriesAsync(organizationId, cancellationToken)).Roots;

    public async Task<CatalogCategory?> GetCategoryAsync(
        int organizationId, string slug, CancellationToken cancellationToken = default)
    {
        var (_, reachable) = BuildTree(await catalog.GetCategoriesAsync(organizationId, cancellationToken));
        return reachable.FirstOrDefault(category => string.Equals(category.Slug, slug.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public Task<IReadOnlyList<CatalogBrand>> GetBrandsAsync(int organizationId, CancellationToken cancellationToken = default)
        => catalog.GetBrandsAsync(organizationId, featuredOnly: false, cancellationToken);

    public async Task<CatalogCollection?> GetCollectionAsync(
        int organizationId, string slug, int limit, int offset, CancellationToken cancellationToken = default)
    {
        var record = await catalog.GetCollectionAsync(slug.Trim(), cancellationToken);
        return record is null ? null : await LoadCollectionAsync(organizationId, record, limit, offset, cancellationToken);
    }

    public Task<PagedResult<ProductCard>> SearchProductsAsync(
        int organizationId, ProductSearchRequest request, CancellationToken cancellationToken = default)
    {
        var sort = Clean(request.Sort)?.ToLowerInvariant() ?? ProductSorts.Featured;
        if (!ProductSorts.Public.Contains(sort))
            throw new BusinessRuleException(
                $"sort must be one of: {string.Join(", ", ProductSorts.Public.Order(StringComparer.Ordinal))}.");
        if (request.MinPrice is decimal min && request.MaxPrice is decimal max && min > max)
            throw new BusinessRuleException("minPrice can't be more than maxPrice.");

        return catalog.GetProductCardsAsync(organizationId, new ProductCardQuery
        {
            Search = Clean(request.Search),
            CategorySlug = Clean(request.Category),
            BrandSlug = Clean(request.Brand),
            TagSlug = Clean(request.Tag),
            MinPrice = request.MinPrice,
            MaxPrice = request.MaxPrice,
            Sort = sort,
            Limit = request.Limit,
            Offset = request.Offset
        }, true, cancellationToken);
    }

    public async Task<ProductDetail?> GetProductAsync(
        int organizationId, string slug, CancellationToken cancellationToken = default)
    {
        var data = await catalog.GetProductDetailAsync(organizationId, slug.Trim(), cancellationToken);
        if (data is null)
            return null;

        data.Detail.FrequentlyBoughtTogether = await GetCardsAsync(
            organizationId, data.FrequentlyBoughtTogetherIds, ThinkCartRules.FrequentlyBoughtTogetherLimit, cancellationToken);
        return data.Detail;
    }

    public async Task<IReadOnlyList<ProductCard>?> GetRelatedProductsAsync(
        int organizationId, string slug, int limit, CancellationToken cancellationToken = default)
    {
        var listing = await catalog.GetVisibleListingAsync(organizationId, slug.Trim(), cancellationToken);
        if (listing is null)
            return null;

        // product_relation 'related' first, then the rest of the same category.
        var related = await GetCardsAsync(
            organizationId,
            await catalog.GetRelatedListingIdsAsync([listing.ListingId], ["related"], cancellationToken),
            limit, cancellationToken);
        if (related.Count >= limit || listing.CategoryId is null)
            return related;

        var sameCategory = await catalog.GetProductCardsAsync(organizationId, new ProductCardQuery
        {
            CategoryId = listing.CategoryId,
            ExcludeIds = related.Select(card => card.ProductId).Append(listing.ListingId).ToList(),
            Limit = limit - related.Count
        }, false, cancellationToken);
        return related.Concat(sameCategory.Items).ToList();
    }

    public async Task<PagedResult<Review>?> GetReviewsAsync(
        int organizationId, string slug, int limit, int offset, CancellationToken cancellationToken = default)
    {
        var listing = await catalog.GetVisibleListingAsync(organizationId, slug.Trim(), cancellationToken);
        return listing is null ? null : await catalog.GetReviewsAsync(listing.ListingId, limit, offset, cancellationToken);
    }

    public async Task<Review?> CreateReviewAsync(
        int organizationId, int? customerId, string slug, ReviewCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var listing = await catalog.GetVisibleListingAsync(organizationId, slug.Trim(), cancellationToken);
        if (listing is null)
            return null;

        var reviewerName = Clean(request.ReviewerName) ?? throw new BusinessRuleException("Your name is required.");
        var comment = Clean(request.Comment) ?? throw new BusinessRuleException("Write a few words about the product.");
        if (request.Rating is < 1 or > 5)
            throw new BusinessRuleException("Rating must be 1 to 5.");

        var email = Clean(request.ReviewerEmail)?.ToLowerInvariant();
        if (email is not null && !EmailPattern().IsMatch(email))
            throw new BusinessRuleException("Enter a valid email address.");

        var imageUrls = (request.ImageUrls ?? []).Select(Clean).OfType<string>().ToList();
        if (imageUrls.Count > ThinkCartRules.MaxReviewImages)
            throw new BusinessRuleException($"Add at most {ThinkCartRules.MaxReviewImages} pictures.");
        if (imageUrls.Any(url => url.Length > 500 ||
                                 !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                                 uri.Scheme is not ("https" or "http")))
            throw new BusinessRuleException("Pictures must be http(s) addresses of at most 500 characters.");

        // Verified purchase: the signed-in customer has a delivered order with a size of this product.
        var orderId = customerId is int id
            ? await catalog.FindDeliveredOrderAsync(id, listing.ListingId, cancellationToken)
            : null;

        return await catalog.CreateReviewAsync(new ReviewDraft
        {
            ListingId = listing.ListingId,
            CustomerId = customerId,
            OrderId = orderId,
            ReviewerName = reviewerName,
            ReviewerEmail = email,
            Rating = request.Rating,
            IsRecommended = request.IsRecommended,
            Comment = comment,
            ImageUrls = imageUrls
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<ProductCard>> SuggestAsync(
        int organizationId, string? query, CancellationToken cancellationToken = default)
    {
        var search = Clean(query);
        if (search is null || search.Length < 2)
            return [];

        return (await catalog.GetProductCardsAsync(organizationId, new ProductCardQuery
        {
            Search = search,
            Sort = ProductSorts.Suggest,
            Limit = ThinkCartRules.SearchSuggestLimit
        }, false, cancellationToken)).Items;
    }

    public Task<StorefrontPage?> GetPageAsync(string slug, CancellationToken cancellationToken = default)
        => catalog.GetPageAsync(slug.Trim(), cancellationToken);

    public async Task RecordEventsAsync(
        int? customerId, AnalyticsEventBatch batch, CancellationToken cancellationToken = default)
    {
        var events = batch.Events ?? [];
        if (events.Count > ThinkCartRules.MaxEventsPerRequest)
            throw new BusinessRuleException($"Send at most {ThinkCartRules.MaxEventsPerRequest} events at a time.");

        Guid? sessionId = null;
        if (Clean(batch.SessionId) is string session)
        {
            if (!Guid.TryParse(session, out var parsed))
                throw new BusinessRuleException("sessionId must be a UUID.");
            sessionId = parsed;
        }

        var now = DateTime.UtcNow;
        foreach (var item in events)
        {
            item.EventName = item.EventName?.Trim() ?? string.Empty;
            if (!AnalyticsEventNames.All.Contains(item.EventName))
                throw new BusinessRuleException(
                    $"Unknown eventName '{item.EventName}'. Use one of: {string.Join(", ", AnalyticsEventNames.All.Order(StringComparer.Ordinal))}.");

            item.OrderNumber = Clean(item.OrderNumber)?.ToUpperInvariant();
            item.SearchQuery = Clean(item.SearchQuery);
            item.PageUrl = Clean(item.PageUrl);

            // Clock-skewed or replayed timestamps are recorded as received.
            if (item.OccurredAt is DateTimeOffset occurredAt &&
                (occurredAt.UtcDateTime > now.AddMinutes(5) || occurredAt.UtcDateTime < now.AddDays(-7)))
                item.OccurredAt = null;
        }

        await catalog.AddEventsAsync(sessionId, customerId, events, now, cancellationToken);
    }

    public async Task<IReadOnlyList<ProductCard>> GetCardsAsync(
        int organizationId, IReadOnlyList<int> listingIds, int limit, CancellationToken cancellationToken = default)
    {
        if (listingIds.Count == 0 || limit <= 0)
            return [];

        return (await catalog.GetProductCardsAsync(organizationId, new ProductCardQuery
        {
            Ids = listingIds.Distinct().ToList(),
            Sort = ProductSorts.IdOrder,
            Limit = limit
        }, false, cancellationToken)).Items;
    }

    private async Task<CatalogCollection> LoadCollectionAsync(
        int organizationId, CollectionRecord record, int limit, int offset, CancellationToken cancellationToken)
    {
        ProductCardQuery query;
        if (record.CollectionType == "smart")
        {
            query = SmartQuery(record.Rules);
        }
        else
        {
            var ids = await catalog.GetCollectionProductIdsAsync(record.CollectionId, cancellationToken);
            query = new ProductCardQuery { Ids = ids, Sort = ProductSorts.IdOrder };
        }

        query.Limit = limit;
        query.Offset = offset;
        var products = await catalog.GetProductCardsAsync(organizationId, query, false, cancellationToken);
        return new CatalogCollection
        {
            CollectionId = record.CollectionId,
            Slug = record.Slug,
            Name = record.Name,
            Description = record.Description,
            ImageUrl = record.ImageUrl,
            Products = products.Items
        };
    }

    /// <summary>
    /// A smart collection's rules, e.g. {"category_id":4,"tag":"organic","sort":"best_selling"}. Also read:
    /// category (slug), brand (slug), brand_id, featured, min_price, max_price, days (best-selling window).
    /// Unknown keys are ignored.
    /// </summary>
    private static ProductCardQuery SmartQuery(string? rules)
    {
        var query = new ProductCardQuery();
        if (string.IsNullOrWhiteSpace(rules))
            return query;

        try
        {
            using var document = JsonDocument.Parse(rules);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return query;

            query.CategoryId = Integer(root, "category_id");
            query.CategorySlug = String(root, "category");
            query.BrandId = Integer(root, "brand_id");
            query.BrandSlug = String(root, "brand");
            query.TagSlug = String(root, "tag");
            query.MinPrice = Number(root, "min_price");
            query.MaxPrice = Number(root, "max_price");
            query.SalesSinceDays = Integer(root, "days");
            query.FeaturedOnly = root.TryGetProperty("featured", out var featured) && featured.ValueKind == JsonValueKind.True;
            var sort = String(root, "sort")?.ToLowerInvariant();
            if (sort is not null && ProductSorts.Public.Contains(sort))
                query.Sort = sort;
        }
        catch (JsonException)
        {
            // Unreadable rules: the collection shows the featured listings.
        }

        return query;
    }

    /// <summary>
    /// Category rows into a tree. A category whose parent is inactive (or missing) is hidden with its
    /// sub-categories. Returns the roots and every category reachable from them.
    /// </summary>
    private static (List<CatalogCategory> Roots, List<CatalogCategory> Reachable) BuildTree(
        IReadOnlyList<CatalogCategory> rows)
    {
        var byId = rows.ToDictionary(row => row.CategoryId);
        var children = rows
            .Where(row => row.ParentId is int parentId && byId.ContainsKey(parentId))
            .GroupBy(row => row.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        var reachable = new List<CatalogCategory>();
        var roots = rows.Where(row => row.ParentId is null).Select(row => Attach(row, depth: 0)).ToList();
        return (roots, reachable);

        CatalogCategory Attach(CatalogCategory row, int depth)
        {
            reachable.Add(row);
            row.Children = depth < 20 && children.TryGetValue(row.CategoryId, out var list)
                ? list.Select(child => Attach(child, depth + 1)).ToList()
                : [];
            return row;
        }
    }

    private static CatalogCategory Copy(CatalogCategory category, bool withChildren) => new()
    {
        CategoryId = category.CategoryId,
        ParentId = category.ParentId,
        Slug = category.Slug,
        Name = category.Name,
        NameLocal = category.NameLocal,
        ImageUrl = category.ImageUrl,
        IconUrl = category.IconUrl,
        IsFeatured = category.IsFeatured,
        Children = withChildren ? category.Children : []
    };

    private static int? Integer(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static decimal? Number(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? number
            : null;

    private static string? String(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? Clean(value.GetString())
            : null;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(ThinkCartRules.EmailPattern, RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
