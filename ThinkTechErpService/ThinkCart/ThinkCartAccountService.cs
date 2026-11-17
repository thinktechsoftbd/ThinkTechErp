using System.Text.RegularExpressions;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.ThinkCart;
using ThinkTechErpRepository.ThinkCart;
using ThinkTechErpService.Security;

namespace ThinkTechErpService.ThinkCart;

public interface IThinkCartAccountService
{
    /// <summary>Null when the signed-in customer no longer exists.</summary>
    Task<AccountDashboard?> GetDashboardAsync(int organizationId, int customerId, CancellationToken cancellationToken = default);

    Task<CustomerProfile?> GetProfileAsync(int organizationId, int customerId, CancellationToken cancellationToken = default);

    Task<CustomerProfile?> UpdateProfileAsync(
        int organizationId, int customerId, CustomerProfileUpdateRequest request, CancellationToken cancellationToken = default);

    /// <returns>False when the current password is wrong (or the customer is a guest without one).</returns>
    Task<bool> ChangePasswordAsync(
        int organizationId, int customerId, CustomerPasswordChangeRequest request, CancellationToken cancellationToken = default);

    Task<PagedResult<OrderSummary>> GetOrdersAsync(
        int organizationId, int customerId, string? status, int limit, int offset, CancellationToken cancellationToken = default);

    Task<OrderDetail?> GetOrderAsync(
        int organizationId, int customerId, string orderNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerAddress>> GetAddressesAsync(int customerId, CancellationToken cancellationToken = default);
    Task<CustomerAddress> CreateAddressAsync(
        int customerId, CustomerAddressUpsertRequest request, CancellationToken cancellationToken = default);
    Task<CustomerAddress?> UpdateAddressAsync(
        int customerId, Guid addressId, CustomerAddressUpsertRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAddressAsync(int customerId, Guid addressId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductCard>> GetWishlistAsync(int organizationId, int customerId, CancellationToken cancellationToken = default);

    /// <returns>False when the organization has no such product.</returns>
    Task<bool> AddToWishlistAsync(int organizationId, int customerId, int productId, CancellationToken cancellationToken = default);

    Task RemoveFromWishlistAsync(int organizationId, int customerId, int productId, CancellationToken cancellationToken = default);
}

public sealed partial class ThinkCartAccountService(
    IThinkCartCustomerRepository customers,
    IThinkCartOrderRepository orders,
    IThinkCartCheckoutRepository checkout,
    IThinkCartCatalogService catalog,
    IThinkCartAuthService auth,
    IPasswordHasher passwordHasher) : IThinkCartAccountService
{
    private const int MaxWishlist = 500;

    public async Task<AccountDashboard?> GetDashboardAsync(
        int organizationId, int customerId, CancellationToken cancellationToken = default)
    {
        var customer = await customers.GetCustomerAsync(organizationId, customerId, cancellationToken);
        if (customer is null)
            return null;

        return new AccountDashboard
        {
            Profile = customer.ToProfile(),
            Stats = await orders.GetStatsAsync(customerId, cancellationToken),
            RecentOrders = (await orders.GetCustomerOrdersAsync(
                organizationId, customerId, null, ThinkCartRules.RecentOrdersLimit, 0, cancellationToken)).Items
        };
    }

    public async Task<CustomerProfile?> GetProfileAsync(
        int organizationId, int customerId, CancellationToken cancellationToken = default)
        => (await customers.GetCustomerAsync(organizationId, customerId, cancellationToken))?.ToProfile();

    public async Task<CustomerProfile?> UpdateProfileAsync(
        int organizationId, int customerId, CustomerProfileUpdateRequest request, CancellationToken cancellationToken = default)
    {
        if (await customers.GetCustomerAsync(organizationId, customerId, cancellationToken) is null)
            return null;

        request.FullName = Clean(request.FullName) ?? throw new BusinessRuleException("Enter your name.");
        request.Email = Clean(request.Email)?.ToLowerInvariant();
        if (request.Email is not null && !EmailPattern().IsMatch(request.Email))
            throw new BusinessRuleException("Enter a valid email address.");
        request.Gender = Clean(request.Gender)?.ToLowerInvariant();
        if (request.Gender is not null && !CustomerGenders.All.Contains(request.Gender))
            throw new BusinessRuleException("gender must be male, female or other.");
        if (request.DateOfBirth is DateOnly dateOfBirth &&
            (dateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow) || dateOfBirth.Year < 1900))
            throw new BusinessRuleException("Enter a valid date of birth.");
        if (request.Email is not null &&
            await customers.EmailInUseAsync(organizationId, request.Email, customerId, cancellationToken))
            throw new BusinessConflictException("Another account uses this email address.");

        await customers.UpdateProfileAsync(customerId, request, cancellationToken);
        return await GetProfileAsync(organizationId, customerId, cancellationToken);
    }

    public async Task<bool> ChangePasswordAsync(
        int organizationId, int customerId, CustomerPasswordChangeRequest request, CancellationToken cancellationToken = default)
    {
        auth.ValidatePassword(request.NewPassword);
        var customer = await customers.GetCustomerAsync(organizationId, customerId, cancellationToken);
        if (!passwordHasher.Verify(request.CurrentPassword, customer?.PasswordHash) || customer is null)
            return false;

        // Sessions stay signed in: the endpoint answers 204 with no new tokens to continue this one with.
        await customers.SetPasswordAsync(
            customerId, passwordHasher.Hash(request.NewPassword), mobileVerified: false, revokeTokens: false,
            CancellationToken.None);
        return true;
    }

    public Task<PagedResult<OrderSummary>> GetOrdersAsync(
        int organizationId, int customerId, string? status, int limit, int offset, CancellationToken cancellationToken = default)
    {
        var filter = Clean(status)?.ToLowerInvariant();
        if (filter is not null && !EcomOrderStatuses.All.Contains(filter))
            throw new BusinessRuleException(
                $"status must be one of: {string.Join(", ", EcomOrderStatuses.All.Order(StringComparer.Ordinal))}.");
        return orders.GetCustomerOrdersAsync(organizationId, customerId, filter, limit, offset, cancellationToken);
    }

    public Task<OrderDetail?> GetOrderAsync(
        int organizationId, int customerId, string orderNumber, CancellationToken cancellationToken = default)
        => orders.GetOrderAsync(organizationId, orderNumber.Trim().ToUpperInvariant(), customerId, null, cancellationToken);

    public Task<IReadOnlyList<CustomerAddress>> GetAddressesAsync(int customerId, CancellationToken cancellationToken = default)
        => customers.GetAddressesAsync(customerId, cancellationToken);

    public async Task<CustomerAddress> CreateAddressAsync(
        int customerId, CustomerAddressUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await NormalizeAsync(request, cancellationToken);
        var addressId = await customers.CreateAddressAsync(customerId, request, cancellationToken);
        return await customers.GetAddressAsync(customerId, addressId, cancellationToken)
               ?? throw new InvalidOperationException("Created address could not be read back.");
    }

    public async Task<CustomerAddress?> UpdateAddressAsync(
        int customerId, Guid addressId, CustomerAddressUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await NormalizeAsync(request, cancellationToken);
        return await customers.UpdateAddressAsync(customerId, addressId, request, cancellationToken)
            ? await customers.GetAddressAsync(customerId, addressId, cancellationToken)
            : null;
    }

    public Task<bool> DeleteAddressAsync(int customerId, Guid addressId, CancellationToken cancellationToken = default)
        => customers.DeleteAddressAsync(customerId, addressId, cancellationToken);

    public async Task<IReadOnlyList<ProductCard>> GetWishlistAsync(
        int organizationId, int customerId, CancellationToken cancellationToken = default)
        => await catalog.GetCardsAsync(
            organizationId, await customers.GetWishlistAsync(customerId, cancellationToken), MaxWishlist, cancellationToken);

    public async Task<bool> AddToWishlistAsync(
        int organizationId, int customerId, int productId, CancellationToken cancellationToken = default)
    {
        // A size is saved as its listing, like the cards show it.
        var listingId = await customers.GetListingIdAsync(organizationId, productId, cancellationToken);
        if (listingId is null)
            return false;

        await customers.AddToWishlistAsync(customerId, listingId.Value, cancellationToken);
        return true;
    }

    public async Task RemoveFromWishlistAsync(
        int organizationId, int customerId, int productId, CancellationToken cancellationToken = default)
    {
        var listingId = await customers.GetListingIdAsync(organizationId, productId, cancellationToken) ?? productId;
        await customers.RemoveFromWishlistAsync(customerId, listingId, cancellationToken);
    }

    private async Task NormalizeAsync(CustomerAddressUpsertRequest request, CancellationToken cancellationToken)
    {
        request.Label = Clean(request.Label);
        request.RecipientName = Clean(request.RecipientName) ?? throw new BusinessRuleException("Enter the recipient's name.");
        request.RecipientPhone = ThinkCartRules.NormalizeMobile(request.RecipientPhone)
                                 ?? throw new BusinessRuleException("Enter a valid mobile number (01XXXXXXXXX).");
        request.AddressLine1 = Clean(request.AddressLine1) ?? throw new BusinessRuleException("Enter the address.");
        request.AddressLine2 = Clean(request.AddressLine2);
        request.Landmark = Clean(request.Landmark);

        var location = await checkout.GetDeliveryLocationAsync(request.DistrictId, request.AreaId, cancellationToken)
                       ?? throw new BusinessRuleException("Choose a district.");
        if (request.AreaId is not null && location.AreaId is null)
            throw new BusinessRuleException("The chosen area isn't in this district.");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(ThinkCartRules.EmailPattern, RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
