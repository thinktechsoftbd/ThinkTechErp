using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.ThinkCart;
using ThinkTechErpRepository.ThinkCart;
using ThinkTechErpService.ThinkCart;

namespace ThinkTechErpAPI.Areas.ThinkCart.Controller;

/// <summary>
/// Checkout: payment methods and shipping zones, delivery locations, quotes (shipping zone, weight-band fee,
/// coupon), placing an order and guest order tracking.
/// </summary>
[Route("api/thinkcart")]
[Authorize(Policy = ThinkCartPolicies.Storefront)]
public sealed class ThinkCartCheckoutController(IThinkCartCheckoutService checkout) : ThinkCartControllerBase
{
    [HttpGet("checkout/options")]
    [ProducesResponseType(typeof(CheckoutOptions), StatusCodes.Status200OK)]
    public async Task<ActionResult<CheckoutOptions>> GetOptions(CancellationToken cancellationToken)
        => Ok(await checkout.GetOptionsAsync(cancellationToken));

    [HttpGet("locations/districts")]
    [ProducesResponseType(typeof(IReadOnlyList<LocationDistrict>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LocationDistrict>>> GetDistricts(CancellationToken cancellationToken)
        => Ok(await checkout.GetDistrictsAsync(cancellationToken));

    [HttpGet("locations/districts/{districtId:int:range(1,32767)}/areas")]
    [ProducesResponseType(typeof(IReadOnlyList<LocationArea>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<LocationArea>>> GetAreas(short districtId, CancellationToken cancellationToken)
    {
        var areas = await checkout.GetAreasAsync(districtId, cancellationToken);
        return areas is null ? NotFoundError("There is no such district.") : Ok(areas);
    }

    /// <summary>
    /// The cart priced now for a delivery address: zone = area zone ?? district zone ?? default zone; fee from
    /// the zone's weight band (free from its free_shipping_min_order); couponMessage says why a coupon didn't apply.
    /// </summary>
    [HttpPost("checkout/quote")]
    [ProducesResponseType(typeof(CheckoutQuote), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CheckoutQuote>> Quote(
        [FromBody] CheckoutQuoteRequest request,
        CancellationToken cancellationToken)
    {
        var quote = await checkout.QuoteAsync(OrganizationId, CustomerId, request, cancellationToken);
        return quote is null ? NotFoundError("The cart doesn't exist or has already been ordered.") : Ok(quote);
    }

    /// <summary>
    /// Places the cart as an order (status pending), re-pricing every line. 409 when a line is out of stock or
    /// the cart was already ordered; 400 when the coupon no longer applies.
    /// </summary>
    [HttpPost("checkout/orders")]
    [ProducesResponseType(typeof(OrderPlaced), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderPlaced>> PlaceOrder(
        [FromBody] PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var outcome = await checkout.PlaceOrderAsync(OrganizationId, CustomerId, request, cancellationToken);
        return outcome.Status switch
        {
            PlaceOrderStatus.Placed => StatusCode(StatusCodes.Status201Created, outcome.Order),
            PlaceOrderStatus.CartNotFound => NotFoundError("The cart doesn't exist."),
            PlaceOrderStatus.CartConverted => Conflict(new { error = "This cart has already been ordered." }),
            _ => Unauthorized(new { error = "The signed-in customer no longer exists. Sign in again." })
        };
    }

    /// <summary>Guest tracking: the order with this number, when it was shipped to this phone.</summary>
    [HttpGet("orders/track")]
    [ProducesResponseType(typeof(OrderDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDetail>> TrackOrder(
        [FromQuery, StringLength(40)] string? orderNumber,
        [FromQuery, StringLength(30)] string? phone,
        CancellationToken cancellationToken)
    {
        var order = await checkout.TrackOrderAsync(OrganizationId, orderNumber, phone, cancellationToken);
        return order is null ? NotFoundError("No order with this number was placed with this phone number.") : Ok(order);
    }
}
