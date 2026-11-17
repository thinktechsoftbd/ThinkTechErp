using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThinkTechErpAPI.Security;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.ThinkCart;
using ThinkTechErpService.ThinkCart;

namespace ThinkTechErpAPI.Areas.ThinkCart.Controller;

/// <summary>The signed-in customer's account: profile, password, orders, addresses and wishlist. Customer tokens only.</summary>
[Route("api/thinkcart/account")]
[Authorize(Policy = ThinkCartPolicies.Customer)]
public sealed class ThinkCartAccountController(IThinkCartAccountService account) : ThinkCartControllerBase
{
    private const string CustomerGone = "This account no longer exists. Sign in again.";

    private int SignedInCustomerId => CustomerId ?? throw new InvalidOperationException("The Customer policy admits customer tokens only.");

    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(AccountDashboard), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AccountDashboard>> GetDashboard(CancellationToken cancellationToken)
    {
        var dashboard = await account.GetDashboardAsync(OrganizationId, SignedInCustomerId, cancellationToken);
        return dashboard is null ? Unauthorized(new { error = CustomerGone }) : Ok(dashboard);
    }

    [HttpGet("profile")]
    [ProducesResponseType(typeof(CustomerProfile), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CustomerProfile>> GetProfile(CancellationToken cancellationToken)
    {
        var profile = await account.GetProfileAsync(OrganizationId, SignedInCustomerId, cancellationToken);
        return profile is null ? Unauthorized(new { error = CustomerGone }) : Ok(profile);
    }

    [HttpPut("profile")]
    [ProducesResponseType(typeof(CustomerProfile), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerProfile>> UpdateProfile(
        [FromBody] CustomerProfileUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var profile = await account.UpdateProfileAsync(OrganizationId, SignedInCustomerId, request, cancellationToken);
        return profile is null ? Unauthorized(new { error = CustomerGone }) : Ok(profile);
    }

    [HttpPut("password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] CustomerPasswordChangeRequest request,
        CancellationToken cancellationToken)
        => await account.ChangePasswordAsync(OrganizationId, SignedInCustomerId, request, cancellationToken)
            ? NoContent()
            : BadRequest(new { error = "The current password is incorrect." });

    [HttpGet("orders")]
    [ProducesResponseType(typeof(PagedResult<OrderSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<OrderSummary>>> GetOrders(
        [FromQuery, StringLength(20)] string? status,
        [FromQuery, Range(1, 100)] int limit = 10,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
        => Ok(await account.GetOrdersAsync(OrganizationId, SignedInCustomerId, status, limit, offset, cancellationToken));

    [HttpGet("orders/{orderNumber:maxlength(40)}")]
    [ProducesResponseType(typeof(OrderDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDetail>> GetOrder(string orderNumber, CancellationToken cancellationToken)
    {
        var order = await account.GetOrderAsync(OrganizationId, SignedInCustomerId, orderNumber, cancellationToken);
        return order is null ? NotFoundError("You have no order with this number.") : Ok(order);
    }

    [HttpGet("addresses")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerAddress>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CustomerAddress>>> GetAddresses(CancellationToken cancellationToken)
        => Ok(await account.GetAddressesAsync(SignedInCustomerId, cancellationToken));

    /// <summary>The first address becomes the default; isDefault moves the default to this one.</summary>
    [HttpPost("addresses")]
    [ProducesResponseType(typeof(CustomerAddress), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CustomerAddress>> CreateAddress(
        [FromBody] CustomerAddressUpsertRequest request,
        CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created,
            await account.CreateAddressAsync(SignedInCustomerId, request, cancellationToken));

    [HttpPut("addresses/{addressId:guid}")]
    [ProducesResponseType(typeof(CustomerAddress), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerAddress>> UpdateAddress(
        Guid addressId,
        [FromBody] CustomerAddressUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var address = await account.UpdateAddressAsync(SignedInCustomerId, addressId, request, cancellationToken);
        return address is null ? NotFoundError("There is no such address.") : Ok(address);
    }

    /// <summary>Deleting the default address makes the oldest remaining one the default.</summary>
    [HttpDelete("addresses/{addressId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAddress(Guid addressId, CancellationToken cancellationToken)
        => await account.DeleteAddressAsync(SignedInCustomerId, addressId, cancellationToken)
            ? NoContent()
            : NotFoundError("There is no such address.");

    [HttpGet("wishlist")]
    [ProducesResponseType(typeof(IReadOnlyList<ProductCard>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProductCard>>> GetWishlist(CancellationToken cancellationToken)
        => Ok(await account.GetWishlistAsync(OrganizationId, SignedInCustomerId, cancellationToken));

    /// <summary>Saves a product (a size is saved as its listing). Idempotent.</summary>
    [HttpPut("wishlist/{productId:int:min(1)}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddToWishlist(int productId, CancellationToken cancellationToken)
        => await account.AddToWishlistAsync(OrganizationId, SignedInCustomerId, productId, cancellationToken)
            ? NoContent()
            : NotFoundError("There is no such product.");

    /// <summary>Idempotent: 204 whether or not the product was saved.</summary>
    [HttpDelete("wishlist/{productId:int:min(1)}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveFromWishlist(int productId, CancellationToken cancellationToken)
    {
        await account.RemoveFromWishlistAsync(OrganizationId, SignedInCustomerId, productId, cancellationToken);
        return NoContent();
    }
}
