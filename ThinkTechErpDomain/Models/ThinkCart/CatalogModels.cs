using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.ThinkCart;

/// <summary>A card ribbon: a tag with tag_type 'badge'.</summary>
public sealed class Badge
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Color { get; set; }
}

/// <summary>A listing product (product with no parent, or the parent of its sizes) with its default size.</summary>
public sealed class ProductCard
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
    public decimal? CompareAtPrice { get; set; }
    public int? DiscountPercent { get; set; }
    public bool InStock { get; set; }
    public IReadOnlyList<Badge> Badges { get; set; } = [];
    public decimal? AverageRating { get; set; }
    public int ReviewCount { get; set; }
}

public sealed class CatalogCategory
{
    public int CategoryId { get; set; }
    public int? ParentId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameLocal { get; set; }
    public string? ImageUrl { get; set; }
    public string? IconUrl { get; set; }
    public bool IsFeatured { get; set; }
    public List<CatalogCategory> Children { get; set; } = [];
}

public sealed class CatalogBrand
{
    public int BrandId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public bool IsFeatured { get; set; }
}

public sealed class Banner
{
    public int BannerId { get; set; }
    public string? Title { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? MobileImageUrl { get; set; }
    public string? LinkUrl { get; set; }
    public string Placement { get; set; } = string.Empty;
}

public sealed class Testimonial
{
    public string Name { get; set; } = string.Empty;
    public string? Designation { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
}

public sealed class CatalogCollection
{
    public int CollectionId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public IReadOnlyList<ProductCard> Products { get; set; } = [];
}

/// <summary>A collection row with what decides its products (manual list or smart rules).</summary>
public sealed class CollectionRecord
{
    public int CollectionId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string CollectionType { get; set; } = "manual";
    public string? Rules { get; set; }
}

public sealed class SiteInfo
{
    public string Name { get; set; } = string.Empty;
    public string? Hotline { get; set; }
    public string? Whatsapp { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public SiteSocial Social { get; set; } = new();
    public bool GuestCheckout { get; set; }
    public bool OtpRequired { get; set; }
}

public sealed class SiteSocial
{
    public string? Facebook { get; set; }
    public string? Instagram { get; set; }
    public string? Youtube { get; set; }
    public string? Tiktok { get; set; }
}

public sealed class HomePage
{
    public IReadOnlyList<Banner> HeroBanners { get; set; } = [];
    public IReadOnlyList<Banner> SideBanners { get; set; } = [];
    public IReadOnlyList<CatalogCategory> FeaturedCategories { get; set; } = [];
    public IReadOnlyList<ProductCard> TopSelling { get; set; } = [];
    public IReadOnlyList<CatalogBrand> FeaturedBrands { get; set; } = [];
    public IReadOnlyList<CatalogCollection> Collections { get; set; } = [];
    public IReadOnlyList<Testimonial> Testimonials { get; set; } = [];
}

/// <summary>One sellable size on the product page.</summary>
public sealed class ProductVariantView
{
    public long VariantId { get; set; }

    /// <summary>The size's own ERP product (stock, cost and code are kept per size).</summary>
    public int ProductId { get; set; }

    public string Sku { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public decimal? PackSize { get; set; }
    public string? PackUnit { get; set; }
    public int WeightG { get; set; }
    public decimal Price { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public int? DiscountPercent { get; set; }
    public bool InStock { get; set; }
    public decimal AvailableQuantity { get; set; }
    public int MinOrderQty { get; set; }
    public int? MaxOrderQty { get; set; }
    public bool IsDefault { get; set; }
    public string? ImageUrl { get; set; }
}

public sealed class ProductDetail
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
    public CatalogBrand? Brand { get; set; }
    public CategoryRef? Category { get; set; }
    public IReadOnlyList<Breadcrumb> Breadcrumbs { get; set; } = [];
    public IReadOnlyList<ProductImageView> Images { get; set; } = [];
    public IReadOnlyList<ProductVariantView> Variants { get; set; } = [];
    public IReadOnlyList<Badge> Badges { get; set; } = [];
    public ProductRating Rating { get; set; } = new();
    public IReadOnlyList<ProductCard> FrequentlyBoughtTogether { get; set; } = [];
}

public sealed class CategoryRef
{
    public int CategoryId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class Breadcrumb
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}

public sealed class ProductImageView
{
    public string Url { get; set; } = string.Empty;
    public string? AltText { get; set; }
    public long? VariantId { get; set; }
}

public sealed class ProductRating
{
    public decimal? Average { get; set; }
    public int Count { get; set; }
    public int RecommendedCount { get; set; }
    public IReadOnlyList<RatingStar> Stars { get; set; } = [];
}

public sealed class RatingStar
{
    public int Rating { get; set; }
    public int Count { get; set; }
}

public sealed class Review
{
    public Guid ReviewId { get; set; }
    public string ReviewerName { get; set; } = string.Empty;
    public int Rating { get; set; }
    public bool? IsRecommended { get; set; }
    public string? Comment { get; set; }
    public IReadOnlyList<string> Images { get; set; } = [];
    public bool IsVerifiedPurchase { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class ReviewCreateRequest
{
    [Required, StringLength(150, MinimumLength = 1)]
    public string ReviewerName { get; set; } = string.Empty;

    [StringLength(ThinkCartRules.EmailMaxLength)]
    public string? ReviewerEmail { get; set; }

    [Range(1, 5)]
    public int Rating { get; set; }

    public bool? IsRecommended { get; set; }

    [Required, StringLength(2000, MinimumLength = 1)]
    public string Comment { get; set; } = string.Empty;

    [MaxLength(ThinkCartRules.MaxReviewImages)]
    public string[]? ImageUrls { get; set; }
}

public sealed class StorefrontPage
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
}

public sealed class AnalyticsEventBatch
{
    [StringLength(64)]
    public string? SessionId { get; set; }

    [Required]
    public List<AnalyticsEventInput> Events { get; set; } = [];
}

public sealed class AnalyticsEventInput
{
    [Required, StringLength(40)]
    public string EventName { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int? ProductId { get; set; }

    [Range(1, long.MaxValue)]
    public long? VariantId { get; set; }

    [Range(1, int.MaxValue)]
    public int? CategoryId { get; set; }

    [StringLength(40)]
    public string? OrderNumber { get; set; }

    [StringLength(200)]
    public string? SearchQuery { get; set; }

    [Range(0, ThinkCartRules.MaxCartQuantity)]
    public int? Quantity { get; set; }

    [Range(typeof(decimal), "0", "9999999999")]
    public decimal? Value { get; set; }

    [StringLength(2000)]
    public string? PageUrl { get; set; }

    public DateTimeOffset? OccurredAt { get; set; }
}

/// <summary>What a product card list is filtered and sorted by. Slugs are matched case-insensitively.</summary>
public sealed class ProductCardQuery
{
    public string? Search { get; set; }
    public string? CategorySlug { get; set; }
    public int? CategoryId { get; set; }
    public string? BrandSlug { get; set; }
    public int? BrandId { get; set; }
    public string? TagSlug { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public bool FeaturedOnly { get; set; }

    /// <summary>Only these listings (with ProductSorts.IdOrder: in this order).</summary>
    public IReadOnlyList<int>? Ids { get; set; }

    public IReadOnlyList<int>? ExcludeIds { get; set; }

    public string Sort { get; set; } = ProductSorts.Featured;

    /// <summary>Best-selling window in days; null counts every order.</summary>
    public int? SalesSinceDays { get; set; }

    /// <summary>Only listings with units sold in the window.</summary>
    public bool SoldOnly { get; set; }

    public int Limit { get; set; } = 24;
    public int Offset { get; set; }
}
