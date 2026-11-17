using ThinkTechErpDomain.Models.ThinkCart;

namespace ThinkTechErpRepository.ThinkCart;

/// <summary>
/// Web customers (supplier rows with is_customer = true) of one organization: accounts, refresh tokens
/// (ecom_customer_token, SHA-256 hashes only), OTP codes (otp_code, hashes only), addresses and wishlist.
/// </summary>
public interface IThinkCartCustomerRepository
{
    /// <summary>
    /// The organization_id of an active integration client's organization (security_organization_client
    /// .org_object_id → organization.object_id); null when the client has none or either is inactive.
    /// </summary>
    Task<int?> GetClientOrganizationIdAsync(string clientName, CancellationToken cancellationToken = default);

    Task<CustomerRecord?> GetCustomerAsync(int organizationId, int customerId, CancellationToken cancellationToken = default);

    /// <summary>The customer with this mobile: one with an account first, then the oldest.</summary>
    Task<CustomerRecord?> FindByMobileAsync(int organizationId, string mobile, CancellationToken cancellationToken = default);

    /// <summary>The customer with this email (case-insensitive): one with an account first, then the oldest.</summary>
    Task<CustomerRecord?> FindByEmailAsync(int organizationId, string email, CancellationToken cancellationToken = default);

    Task<bool> EmailInUseAsync(
        int organizationId, string email, int? excludeCustomerId, CancellationToken cancellationToken = default);

    /// <summary>A new registered customer (code 'WEB-' || mobile); null when that code is taken.</summary>
    Task<int?> CreateCustomerAsync(
        int organizationId, string fullName, string mobile, string? email, string passwordHash, bool mobileVerified,
        CancellationToken cancellationToken = default);

    /// <summary>Turns a guest into a registered customer: name, email, password and registered_at.</summary>
    Task RegisterGuestAsync(
        int customerId, string fullName, string? email, string passwordHash, bool mobileVerified,
        CancellationToken cancellationToken = default);

    Task UpdateProfileAsync(int customerId, CustomerProfileUpdateRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sets the password (registering a guest), and optionally ends every session.</summary>
    Task SetPasswordAsync(
        int customerId, string passwordHash, bool mobileVerified, bool revokeTokens,
        CancellationToken cancellationToken = default);

    Task MarkMobileVerifiedAsync(int customerId, CancellationToken cancellationToken = default);

    /// <returns>The UTC expiry of the new refresh token.</returns>
    Task<DateTime> CreateTokenAsync(
        int customerId, string tokenHash, TimeSpan lifetime, string? createdByIp, CancellationToken cancellationToken = default);

    Task<CustomerTokenRecord?> GetTokenAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Revokes an active refresh token and stores its replacement in one transaction.</summary>
    /// <returns>The replacement's UTC expiry, or null when the old token was no longer active.</returns>
    Task<DateTime?> RotateTokenAsync(
        long tokenId, string newTokenHash, TimeSpan lifetime, string? createdByIp, CancellationToken cancellationToken = default);

    Task RevokeTokenAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task RevokeAllTokensAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>Seconds since the last code for this purpose (null when none in the last hour) and codes sent in the last hour.</summary>
    Task<(int? SecondsSinceLast, int LastHour)> GetOtpThrottleAsync(
        string mobile, string purpose, CancellationToken cancellationToken = default);

    /// <summary>Stores a code hash (earlier unused codes for the purpose stop working).</summary>
    Task CreateOtpAsync(
        string mobile, string purpose, string codeHash, int lifetimeSeconds, string? requestIp,
        CancellationToken cancellationToken = default);

    /// <summary>Checks the latest unused, unexpired code: a match consumes it, a miss counts an attempt.</summary>
    Task<OtpCheckResult> CheckOtpAsync(
        string mobile, string purpose, string codeHash, int maxAttempts, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerAddress>> GetAddressesAsync(int customerId, CancellationToken cancellationToken = default);
    Task<CustomerAddress?> GetAddressAsync(int customerId, Guid addressId, CancellationToken cancellationToken = default);
    Task<Guid> CreateAddressAsync(
        int customerId, CustomerAddressUpsertRequest request, CancellationToken cancellationToken = default);
    Task<bool> UpdateAddressAsync(
        int customerId, Guid addressId, CustomerAddressUpsertRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAddressAsync(int customerId, Guid addressId, CancellationToken cancellationToken = default);

    /// <summary>Wishlist listings, latest first.</summary>
    Task<IReadOnlyList<int>> GetWishlistAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>The listing of a product of the organization (a size maps to its parent); null when there is none.</summary>
    Task<int?> GetListingIdAsync(int organizationId, int productId, CancellationToken cancellationToken = default);

    Task AddToWishlistAsync(int customerId, int listingId, CancellationToken cancellationToken = default);
    Task RemoveFromWishlistAsync(int customerId, int listingId, CancellationToken cancellationToken = default);
}
