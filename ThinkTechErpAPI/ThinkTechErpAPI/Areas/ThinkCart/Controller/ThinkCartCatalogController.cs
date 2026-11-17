using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.ThinkCart;
using ThinkTechErpService.ThinkCart;

namespace ThinkTechErpAPI.Areas.ThinkCart.Controller;

/// <summary>
/// The storefront catalogue: site settings, home page, categories, brands, collections, products, reviews,
/// search suggestions, content pages and analytics events. Products are those of the caller's organization
/// on the website (web_status active), priced now (live campaigns) with stock from stock_balance.
/// </summary>
[Route("api/thinkcart")]
[Authorize(Policy = ThinkCartPolicies.Storefront)]
public sealed class ThinkCartCatalogController(IThinkCartCatalogService catalog) : ThinkCartControllerBase
{
    [HttpGet("site")]
    [ProducesResponseType(typeof(SiteInfo), StatusCodes.Status200OK)]
    public async Task<ActionResult<SiteInfo>> GetSite(CancellationToken cancellationToken)
        => Ok(await catalog.GetSiteAsync(OrganizationId, cancellationToken));

    [HttpGet("home")]
    [ProducesResponseType(typeof(HomePage), StatusCodes.Status200OK)]
    public async Task<ActionResult<HomePage>> GetHome(CancellationToken cancellationToken)
        => Ok(await catalog.GetHomeAsync(OrganizationId, cancellationToken));

    /// <summary>Active categories as a tree, by sort order.</summary>
    [HttpGet("categories")]
    [ProducesResponseType(typeof(IReadOnlyList<CatalogCategory>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CatalogCategory>>> GetCategories(CancellationToken cancellationToken)
        => Ok(await catalog.GetCategoriesAsync(OrganizationId, cancellationToken));

    [HttpGet("categories/{slug:maxlength(160)}")]
    [ProducesResponseType(typeof(CatalogCategory), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CatalogCategory>> GetCategory(string slug, CancellationToken cancellationToken)
    {
        var category = await catalog.GetCategoryAsync(OrganizationId, slug, cancellationToken);
        return category is null ? NotFoundError("There is no such category.") : Ok(category);
    }

    [HttpGet("brands")]
    [ProducesResponseType(typeof(IReadOnlyList<CatalogBrand>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CatalogBrand>>> GetBrands(CancellationToken cancellationToken)
        => Ok(await catalog.GetBrandsAsync(OrganizationId, cancellationToken));

    [HttpGet("collections/{slug:maxlength(200)}")]
    [ProducesResponseType(typeof(CatalogCollection), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CatalogCollection>> GetCollection(
        string slug,
        [FromQuery, Range(1, 100)] int limit = 24,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var collection = await catalog.GetCollectionAsync(OrganizationId, slug, limit, offset, cancellationToken);
        return collection is null ? NotFoundError("There is no such collection.") : Ok(collection);
    }

    /// <summary>
    /// Product cards. category, brand and tag are slugs (category includes its sub-categories and
    /// product_category); sort is featured (default), newest, price_asc, price_desc, best_selling or name;
    /// minPrice / maxPrice compare the default size's price.
    /// </summary>
    [HttpGet("products")]
    [ProducesResponseType(typeof(PagedResult<ProductCard>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ProductCard>>> GetProducts(
        [FromQuery, StringLength(100)] string? search,
        [FromQuery, StringLength(160)] string? category,
        [FromQuery, StringLength(160)] string? brand,
        [FromQuery, StringLength(160)] string? tag,
        [FromQuery, StringLength(20)] string? sort,
        [FromQuery, Range(typeof(decimal), "0", "99999999")] decimal? minPrice,
        [FromQuery, Range(typeof(decimal), "0", "99999999")] decimal? maxPrice,
        [FromQuery, Range(1, 100)] int limit = 24,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await catalog.SearchProductsAsync(OrganizationId, new ProductSearchRequest
        {
            Search = search,
            Category = category,
            Brand = brand,
            Tag = tag,
            Sort = sort,
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            Limit = limit,
            Offset = offset
        }, cancellationToken));

    [HttpGet("products/{slug:maxlength(260)}")]
    [ProducesResponseType(typeof(ProductDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDetail>> GetProduct(string slug, CancellationToken cancellationToken)
    {
        var product = await catalog.GetProductAsync(OrganizationId, slug, cancellationToken);
        return product is null ? NotFoundError("This product isn't available.") : Ok(product);
    }

    /// <summary>product_relation 'related' first, then the same category.</summary>
    [HttpGet("products/{slug:maxlength(260)}/related")]
    [ProducesResponseType(typeof(IReadOnlyList<ProductCard>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ProductCard>>> GetRelatedProducts(
        string slug,
        [FromQuery, Range(1, 50)] int limit = 12,
        CancellationToken cancellationToken = default)
    {
        var related = await catalog.GetRelatedProductsAsync(OrganizationId, slug, limit, cancellationToken);
        return related is null ? NotFoundError("This product isn't available.") : Ok(related);
    }

    /// <summary>Approved reviews, newest first.</summary>
    [HttpGet("products/{slug:maxlength(260)}/reviews")]
    [ProducesResponseType(typeof(PagedResult<Review>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<Review>>> GetReviews(
        string slug,
        [FromQuery, Range(1, 100)] int limit = 10,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var reviews = await catalog.GetReviewsAsync(OrganizationId, slug, limit, offset, cancellationToken);
        return reviews is null ? NotFoundError("This product isn't available.") : Ok(reviews);
    }

    /// <summary>A review waiting for moderation; a signed-in customer's delivered order makes it a verified purchase.</summary>
    [HttpPost("products/{slug:maxlength(260)}/reviews")]
    [ProducesResponseType(typeof(Review), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Review>> CreateReview(
        string slug,
        [FromBody] ReviewCreateRequest request,
        CancellationToken cancellationToken)
    {
        var review = await catalog.CreateReviewAsync(OrganizationId, CustomerId, slug, request, cancellationToken);
        return review is null
            ? NotFoundError("This product isn't available.")
            : StatusCode(StatusCodes.Status201Created, review);
    }

    /// <summary>Up to 8 cards whose name (or brand) contains the text, names starting with it first.</summary>
    [HttpGet("search/suggest")]
    [ProducesResponseType(typeof(IReadOnlyList<ProductCard>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProductCard>>> Suggest(
        [FromQuery, StringLength(100)] string? q,
        CancellationToken cancellationToken)
        => Ok(await catalog.SuggestAsync(OrganizationId, q, cancellationToken));

    [HttpGet("pages/{slug:maxlength(200)}")]
    [ProducesResponseType(typeof(StorefrontPage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StorefrontPage>> GetPage(string slug, CancellationToken cancellationToken)
    {
        var page = await catalog.GetPageAsync(slug, cancellationToken);
        return page is null ? NotFoundError("There is no such page.") : Ok(page);
    }

    /// <summary>Records up to 50 analytics events (analytics_event).</summary>
    [HttpPost("events")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RecordEvents(
        [FromBody] AnalyticsEventBatch request,
        CancellationToken cancellationToken)
    {
        await catalog.RecordEventsAsync(CustomerId, request, cancellationToken);
        return Accepted();
    }
}
