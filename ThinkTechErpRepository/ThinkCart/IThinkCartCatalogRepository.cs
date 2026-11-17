using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.ThinkCart;

namespace ThinkTechErpRepository.ThinkCart;

/// <summary>
/// The storefront catalogue: settings, banners, categories, brands, collections, product cards and pages,
/// reviews and analytics events. ERP tables are read for one organization (@Org).
/// </summary>
public interface IThinkCartCatalogRepository
{
    /// <summary>site_setting values (JSON text) by key.</summary>
    Task<IReadOnlyDictionary<string, string>> GetSiteSettingsAsync(
        IReadOnlyList<string> keys, CancellationToken cancellationToken = default);

    Task<string?> GetOrganizationNameAsync(int organizationId, CancellationToken cancellationToken = default);

    /// <summary>Active banners of these placements showing now, by sort order.</summary>
    Task<IReadOnlyList<Banner>> GetBannersAsync(
        IReadOnlyList<string> placements, CancellationToken cancellationToken = default);

    /// <summary>Active categories with a slug (flat, by sort order); the service builds the tree.</summary>
    Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(int organizationId, CancellationToken cancellationToken = default);

    /// <summary>Active brands with a slug, by sort order.</summary>
    Task<IReadOnlyList<CatalogBrand>> GetBrandsAsync(
        int organizationId, bool featuredOnly, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Testimonial>> GetTestimonialsAsync(CancellationToken cancellationToken = default);

    Task<CollectionRecord?> GetCollectionAsync(string slug, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CollectionRecord>> GetHomeCollectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>A manual collection's products in their sort order.</summary>
    Task<IReadOnlyList<int>> GetCollectionProductIdsAsync(int collectionId, CancellationToken cancellationToken = default);

    /// <summary>Visible listings as cards (default size priced now), filtered, sorted and paged.</summary>
    Task<PagedResult<ProductCard>> GetProductCardsAsync(
        int organizationId, ProductCardQuery query, bool includeTotal, CancellationToken cancellationToken = default);

    /// <summary>A visible listing's page, without frequently bought together; null when not on sale.</summary>
    Task<ProductDetailData?> GetProductDetailAsync(
        int organizationId, string slug, CancellationToken cancellationToken = default);

    /// <summary>A visible listing by slug: its id and reporting category; null when not on sale.</summary>
    Task<ListingRef?> GetVisibleListingAsync(int organizationId, string slug, CancellationToken cancellationToken = default);

    /// <summary>Products related to these listings (product_relation), by type order then sort order.</summary>
    Task<IReadOnlyList<int>> GetRelatedListingIdsAsync(
        IReadOnlyList<int> listingIds, IReadOnlyList<string> relationTypes, CancellationToken cancellationToken = default);

    /// <summary>Approved reviews, newest first.</summary>
    Task<PagedResult<Review>> GetReviewsAsync(
        int listingId, int limit, int offset, CancellationToken cancellationToken = default);

    /// <summary>The customer's latest delivered order with a size of this listing (verified purchase).</summary>
    Task<long?> FindDeliveredOrderAsync(int customerId, int listingId, CancellationToken cancellationToken = default);

    Task<Review> CreateReviewAsync(ReviewDraft draft, CancellationToken cancellationToken = default);

    Task<StorefrontPage?> GetPageAsync(string slug, CancellationToken cancellationToken = default);

    Task AddEventsAsync(
        Guid? sessionId, int? customerId, IReadOnlyList<AnalyticsEventInput> events, DateTime nowUtc,
        CancellationToken cancellationToken = default);
}

public sealed record ListingRef(int ListingId, int? CategoryId);

public sealed record ProductDetailData(ProductDetail Detail, IReadOnlyList<int> FrequentlyBoughtTogetherIds);

public sealed class ReviewDraft
{
    public int ListingId { get; set; }
    public int? CustomerId { get; set; }
    public long? OrderId { get; set; }
    public string ReviewerName { get; set; } = string.Empty;
    public string? ReviewerEmail { get; set; }
    public int Rating { get; set; }
    public bool? IsRecommended { get; set; }
    public string Comment { get; set; } = string.Empty;
    public IReadOnlyList<string> ImageUrls { get; set; } = [];
}
