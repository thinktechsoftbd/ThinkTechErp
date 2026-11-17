using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.ThinkCart;
using ThinkTechErpRepository.ThinkCart;
using ThinkTechErpService.Security;

namespace ThinkTechErpService.ThinkCart;

public interface IThinkCartAuthService
{
    /// <summary>
    /// Registers a customer. A mobile that belongs to a guest (orders, no password) needs a valid register OTP,
    /// so nobody can take over someone else's order history; one that already has an account is a 409.
    /// </summary>
    Task<CustomerAuthResponse> RegisterAsync(
        int organizationId, CustomerRegisterRequest request, string? clientIp, CancellationToken cancellationToken = default);

    Task<CustomerAuthResult> LoginAsync(
        int organizationId, CustomerLoginRequest request, string? clientIp, CancellationToken cancellationToken = default);

    Task<OtpSendResult> RequestOtpAsync(
        int organizationId, OtpSendRequest request, string? clientIp, CancellationToken cancellationToken = default);

    Task<CustomerAuthResult> OtpLoginAsync(
        int organizationId, OtpLoginRequest request, string? clientIp, CancellationToken cancellationToken = default);

    /// <returns>False when the mobile has no account or the code is wrong or expired.</returns>
    Task<bool> ResetPasswordAsync(
        int organizationId, OtpPasswordResetRequest request, CancellationToken cancellationToken = default);

    /// <summary>Rotates the refresh token; null when it is invalid, expired, or of another organization.</summary>
    Task<CustomerAuthResponse?> RefreshAsync(
        int organizationId, string refreshToken, string? clientIp, CancellationToken cancellationToken = default);

    Task LogoutAsync(string refreshToken);

    /// <summary>6 to 128 characters (bcrypt).</summary>
    void ValidatePassword(string password);
}

public sealed partial class ThinkCartAuthService(
    IThinkCartCustomerRepository customers,
    IPasswordHasher passwordHasher,
    IThinkCartCustomerTokenIssuer tokenIssuer,
    IThinkCartOtpSender otpSender,
    ThinkCartTokenSettings settings) : IThinkCartAuthService
{
    private const int MaxIpLength = 45;

    public async Task<CustomerAuthResponse> RegisterAsync(
        int organizationId, CustomerRegisterRequest request, string? clientIp, CancellationToken cancellationToken = default)
    {
        var fullName = Clean(request.FullName) ?? throw new BusinessRuleException("Enter your name.");
        var mobile = RequireMobile(request.Mobile);
        var email = NormalizeEmail(request.Email);
        ValidatePassword(request.Password);

        var existing = await customers.FindByMobileAsync(organizationId, mobile, cancellationToken);
        if (existing is { IsGuest: false })
            throw new BusinessConflictException("An account with this mobile number already exists. Sign in instead.");
        if (existing is { IsActive: false } or { IsBlocked: true })
            throw new BusinessRuleException("This mobile number can't be registered online. Please call us.");
        if (email is not null && await customers.EmailInUseAsync(organizationId, email, existing?.Id, cancellationToken))
            throw new BusinessConflictException("Another account uses this email address.");

        var code = Clean(request.OtpCode);
        if (existing is not null && code is null)
            throw new BusinessRuleException(
                "This mobile number already has orders. Enter the verification code sent to it to create the account.");

        var verified = false;
        if (code is not null)
        {
            if (await customers.CheckOtpAsync(mobile, OtpPurposes.Register, HashOtp(mobile, OtpPurposes.Register, code),
                    ThinkCartRules.OtpMaxAttempts, cancellationToken) != OtpCheckResult.Valid)
                throw new BusinessRuleException("The verification code is wrong or has expired. Ask for a new one.");
            verified = true;
        }

        var passwordHash = passwordHasher.Hash(request.Password);
        int customerId;
        if (existing is not null)
        {
            await customers.RegisterGuestAsync(existing.Id, fullName, email, passwordHash, verified, cancellationToken);
            customerId = existing.Id;
        }
        else
        {
            customerId = await customers.CreateCustomerAsync(
                             organizationId, fullName, mobile, email, passwordHash, verified, cancellationToken)
                         ?? throw new BusinessConflictException("An account with this mobile number already exists. Sign in instead.");
        }

        var customer = await customers.GetCustomerAsync(organizationId, customerId, cancellationToken)
                       ?? throw new InvalidOperationException("Registered customer could not be read back.");
        return await StartSessionAsync(customer, clientIp, cancellationToken);
    }

    public async Task<CustomerAuthResult> LoginAsync(
        int organizationId, CustomerLoginRequest request, string? clientIp, CancellationToken cancellationToken = default)
    {
        var login = request.Login.Trim();
        CustomerRecord? customer = null;
        if (login.Contains('@'))
            customer = await customers.FindByEmailAsync(organizationId, login.ToLowerInvariant(), cancellationToken);
        else if (ThinkCartRules.NormalizeMobile(login) is string mobile)
            customer = await customers.FindByMobileAsync(organizationId, mobile, cancellationToken);

        // Verify even when nobody matches (or it's a guest without a password), so timing reveals nothing.
        if (!passwordHasher.Verify(request.Password, customer?.PasswordHash) || customer is null)
            return new CustomerAuthResult(CustomerAuthStatus.InvalidCredentials);
        if (!customer.IsActive || customer.IsBlocked)
            return new CustomerAuthResult(CustomerAuthStatus.Blocked);

        return new CustomerAuthResult(CustomerAuthStatus.Succeeded, await StartSessionAsync(customer, clientIp, cancellationToken));
    }

    public async Task<OtpSendResult> RequestOtpAsync(
        int organizationId, OtpSendRequest request, string? clientIp, CancellationToken cancellationToken = default)
    {
        var mobile = RequireMobile(request.Mobile);
        var purpose = request.Purpose.Trim().ToLowerInvariant();
        if (!OtpPurposes.Requestable.Contains(purpose))
            throw new BusinessRuleException("purpose must be login, register or reset_password.");

        var existing = await customers.FindByMobileAsync(organizationId, mobile, cancellationToken);
        if (purpose == OtpPurposes.Register && existing is { IsGuest: false })
            return new OtpSendResult(OtpSendStatus.AlreadyRegistered);
        if (purpose != OtpPurposes.Register && existing is null)
            return new OtpSendResult(OtpSendStatus.NoAccount);

        var (secondsSinceLast, lastHour) = await customers.GetOtpThrottleAsync(mobile, purpose, cancellationToken);
        if (secondsSinceLast is int seconds && seconds < ThinkCartRules.OtpResendSeconds)
            return new OtpSendResult(OtpSendStatus.TooSoon, RetryAfterSeconds: ThinkCartRules.OtpResendSeconds - seconds);
        if (lastHour >= ThinkCartRules.OtpMaxPerHour)
            return new OtpSendResult(OtpSendStatus.TooMany, RetryAfterSeconds: 3600);

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        await customers.CreateOtpAsync(
            mobile, purpose, HashOtp(mobile, purpose, code), ThinkCartRules.OtpLifetimeSeconds, ValidIp(clientIp),
            cancellationToken);
        await otpSender.SendAsync(
            mobile, code, purpose, TimeSpan.FromSeconds(ThinkCartRules.OtpLifetimeSeconds), cancellationToken);
        return new OtpSendResult(OtpSendStatus.Sent, ThinkCartRules.OtpLifetimeSeconds);
    }

    public async Task<CustomerAuthResult> OtpLoginAsync(
        int organizationId, OtpLoginRequest request, string? clientIp, CancellationToken cancellationToken = default)
    {
        var mobile = ThinkCartRules.NormalizeMobile(request.Mobile);
        var code = Clean(request.Code);
        if (mobile is null || code is null)
            return new CustomerAuthResult(CustomerAuthStatus.InvalidCredentials);

        var customer = await customers.FindByMobileAsync(organizationId, mobile, cancellationToken);
        if (customer is null)
            return new CustomerAuthResult(CustomerAuthStatus.InvalidCredentials);

        if (await customers.CheckOtpAsync(mobile, OtpPurposes.Login, HashOtp(mobile, OtpPurposes.Login, code),
                ThinkCartRules.OtpMaxAttempts, cancellationToken) != OtpCheckResult.Valid)
            return new CustomerAuthResult(CustomerAuthStatus.InvalidCredentials);
        if (!customer.IsActive || customer.IsBlocked)
            return new CustomerAuthResult(CustomerAuthStatus.Blocked);

        await customers.MarkMobileVerifiedAsync(customer.Id, cancellationToken);
        return new CustomerAuthResult(CustomerAuthStatus.Succeeded, await StartSessionAsync(customer, clientIp, cancellationToken));
    }

    public async Task<bool> ResetPasswordAsync(
        int organizationId, OtpPasswordResetRequest request, CancellationToken cancellationToken = default)
    {
        ValidatePassword(request.NewPassword);
        var mobile = ThinkCartRules.NormalizeMobile(request.Mobile);
        var code = Clean(request.Code);
        if (mobile is null || code is null)
            return false;

        var customer = await customers.FindByMobileAsync(organizationId, mobile, cancellationToken);
        if (customer is null || !customer.IsActive || customer.IsBlocked)
            return false;
        if (await customers.CheckOtpAsync(mobile, OtpPurposes.ResetPassword,
                HashOtp(mobile, OtpPurposes.ResetPassword, code), ThinkCartRules.OtpMaxAttempts, cancellationToken)
            != OtpCheckResult.Valid)
            return false;

        // A guest who sets a password this way has proved the number, so the account becomes theirs.
        // Every session ends: whoever knew the old password is signed out.
        await customers.SetPasswordAsync(
            customer.Id, passwordHasher.Hash(request.NewPassword), mobileVerified: true, revokeTokens: true,
            CancellationToken.None);
        return true;
    }

    public async Task<CustomerAuthResponse?> RefreshAsync(
        int organizationId, string refreshToken, string? clientIp, CancellationToken cancellationToken = default)
    {
        var record = await customers.GetTokenAsync(OpaqueToken.Hash(refreshToken.Trim()), cancellationToken);
        if (record is null || record.IsExpired)
            return null;

        if (record.IsRevoked)
        {
            // A rotated-out token came back. Unless a parallel refresh rotated it moments ago, it may
            // have been stolen, so every session of the customer ends.
            if (!record.WasJustRotated)
                await customers.RevokeAllTokensAsync(record.CustomerId, CancellationToken.None);
            return null;
        }

        var customer = await customers.GetCustomerAsync(organizationId, record.CustomerId, cancellationToken);
        if (customer is null)
            return null;
        if (!customer.IsActive || customer.IsBlocked)
        {
            await customers.RevokeAllTokensAsync(customer.Id, CancellationToken.None);
            return null;
        }

        // Issue the access token first so rotation is the last step before answering (see AuthService).
        var accessToken = tokenIssuer.Issue(new ThinkCartCustomerIdentity(customer.Id, customer.ObjectId, customer.OrganizationId));
        var newRefreshToken = OpaqueToken.Create();
        var expiresAt = await customers.RotateTokenAsync(
            record.Id, OpaqueToken.Hash(newRefreshToken), settings.RefreshTokenLifetime, TrimIp(clientIp), cancellationToken);
        return expiresAt is null ? null : Response(customer, accessToken, newRefreshToken, expiresAt.Value);
    }

    // Deliberately not cancellable: once asked to end a session, finish doing so.
    public Task LogoutAsync(string refreshToken)
        => customers.RevokeTokenAsync(OpaqueToken.Hash(refreshToken.Trim()), CancellationToken.None);

    public void ValidatePassword(string password)
    {
        if (password.Length is < ThinkCartRules.MinPasswordLength or > ThinkCartRules.MaxPasswordLength)
            throw new BusinessRuleException(
                $"Password must be {ThinkCartRules.MinPasswordLength} to {ThinkCartRules.MaxPasswordLength} characters.");
    }

    private async Task<CustomerAuthResponse> StartSessionAsync(
        CustomerRecord customer, string? clientIp, CancellationToken cancellationToken)
    {
        var accessToken = tokenIssuer.Issue(new ThinkCartCustomerIdentity(customer.Id, customer.ObjectId, customer.OrganizationId));
        var refreshToken = OpaqueToken.Create();
        var expiresAt = await customers.CreateTokenAsync(
            customer.Id, OpaqueToken.Hash(refreshToken), settings.RefreshTokenLifetime, TrimIp(clientIp), cancellationToken);
        return Response(customer, accessToken, refreshToken, expiresAt);
    }

    private CustomerAuthResponse Response(
        CustomerRecord customer, IssuedAccessToken accessToken, string refreshToken, DateTime refreshExpiresAt) => new()
    {
        AccessToken = accessToken.Token,
        ExpiresIn = (int)settings.AccessTokenLifetime.TotalSeconds,
        RefreshToken = refreshToken,
        RefreshTokenExpiresAt = refreshExpiresAt,
        Customer = customer.ToProfile()
    };

    /// <summary>Codes are stored as SHA-256 of mobile, purpose and code, never in plain text.</summary>
    internal static string HashOtp(string mobile, string purpose, string code)
        => OpaqueToken.Hash($"{mobile}:{purpose}:{code}");

    private static string RequireMobile(string? value)
        => ThinkCartRules.NormalizeMobile(value) ?? throw new BusinessRuleException("Enter a valid mobile number (01XXXXXXXXX).");

    private static string? NormalizeEmail(string? value)
    {
        var email = Clean(value)?.ToLowerInvariant();
        if (email is not null && !EmailPattern().IsMatch(email))
            throw new BusinessRuleException("Enter a valid email address.");
        return email;
    }

    private static string? ValidIp(string? clientIp)
        => IPAddress.TryParse(clientIp, out var address) ? address.ToString() : null;

    private static string? TrimIp(string? clientIp)
        => clientIp is { Length: > MaxIpLength } ? clientIp[..MaxIpLength] : clientIp;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(ThinkCartRules.EmailPattern, RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
