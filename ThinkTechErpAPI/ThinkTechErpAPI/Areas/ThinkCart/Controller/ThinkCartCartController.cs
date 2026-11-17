using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.ThinkCart;
using ThinkTechErpService.ThinkCart;

namespace ThinkTechErpAPI.Areas.ThinkCart.Controller;

/// <summary>
/// Carts, addressed by cart.object_id (kept by the storefront in a cookie). Lines are priced now; a signed-in
/// customer's calls attach the cart to them.
/// </summary>
[Route("api/thinkcart/carts")]
[Authorize(Policy = ThinkCartPolicies.Storefront)]
public sealed class ThinkCartCartController(IThinkCartCartService carts) : ThinkCartControllerBase
{
    private const string CartNotFound = "The cart doesn't exist or has already been ordered.";

    [HttpPost]
    [ProducesResponseType(typeof(Cart), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Cart>> CreateCart(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CartCreateRequest? request,
        CancellationToken cancellationToken)
    {
        var cart = await carts.CreateCartAsync(OrganizationId, CustomerId, request ?? new CartCreateRequest(), cancellationToken);
        return CreatedAtAction(nameof(GetCart), new { cartId = cart.CartId }, cart);
    }

    [HttpGet("{cartId:guid}")]
    [ProducesResponseType(typeof(Cart), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Cart>> GetCart(Guid cartId, CancellationToken cancellationToken)
    {
        var cart = await carts.GetCartAsync(OrganizationId, cartId, cancellationToken);
        return cart is null ? NotFoundError(CartNotFound) : Ok(cart);
    }

    /// <summary>Sets a size's quantity: 0 removes it; clamped to its min/max order quantity; 409 when out of stock.</summary>
    [HttpPut("{cartId:guid}/items")]
    [ProducesResponseType(typeof(Cart), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Cart>> SetItem(
        Guid cartId,
        [FromBody] CartLineInput request,
        CancellationToken cancellationToken)
    {
        var cart = await carts.SetItemAsync(OrganizationId, CustomerId, cartId, request, cancellationToken);
        return cart is null ? NotFoundError(CartNotFound) : Ok(cart);
    }

    [HttpDelete("{cartId:guid}/items/{variantId:long:min(1)}")]
    [ProducesResponseType(typeof(Cart), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Cart>> RemoveItem(Guid cartId, long variantId, CancellationToken cancellationToken)
    {
        var cart = await carts.RemoveItemAsync(OrganizationId, CustomerId, cartId, variantId, cancellationToken);
        return cart is null ? NotFoundError(CartNotFound) : Ok(cart);
    }

    /// <summary>Incomplete-order capture while the customer fills in the checkout form.</summary>
    [HttpPut("{cartId:guid}/contact")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateContact(
        Guid cartId,
        [FromBody] CartContactRequest request,
        CancellationToken cancellationToken)
        => await carts.UpdateContactAsync(cartId, request, cancellationToken)
            ? NoContent()
            : NotFoundError(CartNotFound);

    /// <summary>"You may also like": frequently bought together and related products of the cart's items.</summary>
    [HttpGet("{cartId:guid}/recommendations")]
    [ProducesResponseType(typeof(IReadOnlyList<ProductCard>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ProductCard>>> GetRecommendations(
        Guid cartId,
        [FromQuery, Range(1, 50)] int limit = 8,
        CancellationToken cancellationToken = default)
    {
        var cards = await carts.GetRecommendationsAsync(OrganizationId, cartId, limit, cancellationToken);
        return cards is null ? NotFoundError(CartNotFound) : Ok(cards);
    }
}
