using System.Security.Cryptography;
using System.Text;
using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.ThinkCart;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.ThinkCart;

public sealed class ThinkCartCustomerRepository(IDbConnectionFactory connectionFactory) : IThinkCartCustomerRepository
{
    private const string CustomerSelect = """
        SELECT supplier_id AS Id,
               object_id AS ObjectId,
               organization_id AS OrganizationId,
               supplier_name AS FullName,
               mobile AS Mobile,
               email AS Email,
               gender AS Gender,
               date_of_birth AS DateOfBirth,
               registered_at AT TIME ZONE 'utc' AS RegisteredAt,
               password_hash AS PasswordHash,
               is_active AS IsActive,
               blocked_at IS NOT NULL AS IsBlocked
        FROM public.supplier
        WHERE organization_id = @Org AND is_customer
        """;

    private const string AddressSelect = """
        SELECT a.object_id AS AddressId,
               a.label AS Label,
               coalesce(a.recipient_name, '') AS RecipientName,
               coalesce(a.recipient_phone, '') AS RecipientPhone,
               a.district_id AS DistrictId,
               d.name AS DistrictName,
               a.area_id AS AreaId,
               ar.name AS AreaName,
               a.address_line1 AS AddressLine1,
               a.address_line2 AS AddressLine2,
               a.landmark AS Landmark,
               a.is_default AS IsDefault
        FROM public.supplier_address a
        JOIN public.district d ON d.id = a.district_id
        LEFT JOIN public.area ar ON ar.id = a.area_id
        """;

    public async Task<int?> GetClientOrganizationIdAsync(string clientName, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition("""
            SELECT o.organization_id
            FROM public.security_organization_client c
            JOIN public.organization o ON o.object_id = c.org_object_id
            WHERE c.client_name = @ClientName AND c.is_active AND o.is_active
            """, new { ClientName = clientName }, cancellationToken: cancellationToken));
    }

    public async Task<CustomerRecord?> GetCustomerAsync(
        int organizationId, int customerId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<CustomerRecord>(new CommandDefinition(
            $"{CustomerSelect} AND supplier_id = @CustomerId",
            new { Org = organizationId, CustomerId = customerId }, cancellationToken: cancellationToken));
    }

    public async Task<CustomerRecord?> FindByMobileAsync(
        int organizationId, string mobile, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<CustomerRecord>(new CommandDefinition($"""
            {CustomerSelect} AND mobile = @Mobile
            ORDER BY (password_hash IS NOT NULL) DESC, supplier_id
            LIMIT 1
            """, new { Org = organizationId, Mobile = mobile }, cancellationToken: cancellationToken));
    }

    public async Task<CustomerRecord?> FindByEmailAsync(
        int organizationId, string email, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<CustomerRecord>(new CommandDefinition($"""
            {CustomerSelect} AND lower(email) = lower(@Email)
            ORDER BY (password_hash IS NOT NULL) DESC, supplier_id
            LIMIT 1
            """, new { Org = organizationId, Email = email }, cancellationToken: cancellationToken));
    }

    public async Task<bool> EmailInUseAsync(
        int organizationId, string email, int? excludeCustomerId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1 FROM public.supplier
                WHERE organization_id = @Org AND is_customer AND lower(email) = lower(@Email)
                  AND (CAST(@ExcludeId AS integer) IS NULL OR supplier_id <> @ExcludeId))
            """, new { Org = organizationId, Email = email, ExcludeId = excludeCustomerId },
            cancellationToken: cancellationToken));
    }

    public async Task<int?> CreateCustomerAsync(
        int organizationId, string fullName, string mobile, string? email, string passwordHash, bool mobileVerified,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await InsertCustomerAsync(
            connection, null, organizationId, fullName, mobile, email, passwordHash, mobileVerified, cancellationToken);
    }

    public async Task RegisterGuestAsync(
        int customerId, string fullName, string? email, string passwordHash, bool mobileVerified,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.supplier
            SET supplier_name = @FullName,
                email = coalesce(@Email, email),
                password_hash = @PasswordHash,
                registered_at = coalesce(registered_at, {SecurityDb.NowUtc}),
                mobile_verified_at = CASE WHEN @MobileVerified THEN {SecurityDb.NowUtc} ELSE mobile_verified_at END,
                updated_at = {SecurityDb.NowUtc}
            WHERE supplier_id = @CustomerId AND is_customer
            """, new
        {
            CustomerId = customerId,
            FullName = fullName,
            Email = email,
            PasswordHash = passwordHash,
            MobileVerified = mobileVerified
        }, cancellationToken: cancellationToken));
    }

    public async Task UpdateProfileAsync(
        int customerId, CustomerProfileUpdateRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.supplier
            SET supplier_name = @FullName,
                email = @Email,
                gender = @Gender,
                date_of_birth = @DateOfBirth,
                updated_at = {SecurityDb.NowUtc}
            WHERE supplier_id = @CustomerId AND is_customer
            """, new
        {
            CustomerId = customerId,
            request.FullName,
            request.Email,
            request.Gender,
            request.DateOfBirth
        }, cancellationToken: cancellationToken));
    }

    public async Task SetPasswordAsync(
        int customerId, string passwordHash, bool mobileVerified, bool revokeTokens,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.supplier
            SET password_hash = @PasswordHash,
                registered_at = coalesce(registered_at, {SecurityDb.NowUtc}),
                mobile_verified_at = CASE WHEN @MobileVerified THEN {SecurityDb.NowUtc} ELSE mobile_verified_at END,
                updated_at = {SecurityDb.NowUtc}
            WHERE supplier_id = @CustomerId AND is_customer
            """, new { CustomerId = customerId, PasswordHash = passwordHash, MobileVerified = mobileVerified },
            transaction, cancellationToken: cancellationToken));
        if (revokeTokens)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE public.ecom_customer_token SET revoked_at = now()
                WHERE customer_id = @CustomerId AND revoked_at IS NULL
                """, new { CustomerId = customerId }, transaction, cancellationToken: cancellationToken));
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MarkMobileVerifiedAsync(int customerId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.supplier
            SET mobile_verified_at = {SecurityDb.NowUtc}
            WHERE supplier_id = @CustomerId AND is_customer
            """, new { CustomerId = customerId }, cancellationToken: cancellationToken));
    }

    public async Task<DateTime> CreateTokenAsync(
        int customerId, string tokenHash, TimeSpan lifetime, string? createdByIp, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // Keep the table small. Revoked tokens stay until they expire, so a stolen, already rotated
        // token is still recognized if it comes back.
        await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM public.ecom_customer_token WHERE customer_id = @CustomerId AND expires_at < now()
            """, new { CustomerId = customerId }, cancellationToken: cancellationToken));
        return await InsertTokenAsync(connection, null, customerId, tokenHash, lifetime, createdByIp, cancellationToken);
    }

    public async Task<CustomerTokenRecord?> GetTokenAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<CustomerTokenRecord>(new CommandDefinition("""
            SELECT id AS Id,
                   customer_id AS CustomerId,
                   expires_at <= now() AS IsExpired,
                   revoked_at IS NOT NULL AS IsRevoked,
                   coalesce(replaced_by_token_hash IS NOT NULL AND revoked_at > now() - interval '30 seconds', false)
                       AS WasJustRotated
            FROM public.ecom_customer_token
            WHERE token_hash = @TokenHash
            """, new { TokenHash = tokenHash }, cancellationToken: cancellationToken));
    }

    public async Task<DateTime?> RotateTokenAsync(
        long tokenId, string newTokenHash, TimeSpan lifetime, string? createdByIp, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var customerId = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition("""
            UPDATE public.ecom_customer_token
            SET revoked_at = now(), replaced_by_token_hash = @NewTokenHash
            WHERE id = @TokenId AND revoked_at IS NULL AND expires_at > now()
            RETURNING customer_id
            """, new { TokenId = tokenId, NewTokenHash = newTokenHash }, transaction, cancellationToken: cancellationToken));
        if (customerId is null)
            return null;

        var expiresAt = await InsertTokenAsync(
            connection, transaction, customerId.Value, newTokenHash, lifetime, createdByIp, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return expiresAt;
    }

    public async Task RevokeTokenAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE public.ecom_customer_token SET revoked_at = now()
            WHERE token_hash = @TokenHash AND revoked_at IS NULL
            """, new { TokenHash = tokenHash }, cancellationToken: cancellationToken));
    }

    public async Task RevokeAllTokensAsync(int customerId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE public.ecom_customer_token SET revoked_at = now()
            WHERE customer_id = @CustomerId AND revoked_at IS NULL
            """, new { CustomerId = customerId }, cancellationToken: cancellationToken));
    }

    public async Task<(int? SecondsSinceLast, int LastHour)> GetOtpThrottleAsync(
        string mobile, string purpose, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<(int?, int)>(new CommandDefinition("""
            SELECT floor(extract(epoch FROM now() - max(created_at) FILTER (WHERE purpose = @Purpose)))::int,
                   count(*)::int
            FROM public.otp_code
            WHERE phone = @Phone AND created_at > now() - interval '1 hour'
            """, new { Phone = mobile, Purpose = purpose }, cancellationToken: cancellationToken));
    }

    public async Task CreateOtpAsync(
        string mobile, string purpose, string codeHash, int lifetimeSeconds, string? requestIp,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE public.otp_code SET consumed_at = now()
            WHERE phone = @Phone AND purpose = @Purpose AND consumed_at IS NULL;

            INSERT INTO public.otp_code (phone, purpose, code_hash, expires_at, request_ip)
            VALUES (@Phone, @Purpose, @CodeHash, now() + make_interval(secs => @Seconds), CAST(@Ip AS inet));
            """, new { Phone = mobile, Purpose = purpose, CodeHash = codeHash, Seconds = lifetimeSeconds, Ip = requestIp },
            transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<OtpCheckResult> CheckOtpAsync(
        string mobile, string purpose, string codeHash, int maxAttempts, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var code = await connection.QuerySingleOrDefaultAsync<OtpRow>(new CommandDefinition("""
            SELECT id AS Id, code_hash AS CodeHash, attempts::int AS Attempts
            FROM public.otp_code
            WHERE phone = @Phone AND purpose = @Purpose AND consumed_at IS NULL AND expires_at > now()
            ORDER BY created_at DESC, id DESC
            LIMIT 1
            FOR UPDATE
            """, new { Phone = mobile, Purpose = purpose }, transaction, cancellationToken: cancellationToken));
        if (code is null)
            return OtpCheckResult.Invalid;
        if (code.Attempts >= maxAttempts)
            return OtpCheckResult.TooManyAttempts;

        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(code.CodeHash), Encoding.ASCII.GetBytes(codeHash));
        await connection.ExecuteAsync(new CommandDefinition(matches
                ? "UPDATE public.otp_code SET consumed_at = now() WHERE id = @Id"
                : "UPDATE public.otp_code SET attempts = attempts + 1 WHERE id = @Id",
            new { code.Id }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return matches ? OtpCheckResult.Valid : OtpCheckResult.Invalid;
    }

    public async Task<IReadOnlyList<CustomerAddress>> GetAddressesAsync(
        int customerId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<CustomerAddress>(new CommandDefinition($"""
            {AddressSelect}
            WHERE a.supplier_id = @CustomerId
            ORDER BY a.is_default DESC, a.created_at, a.id
            """, new { CustomerId = customerId }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<CustomerAddress?> GetAddressAsync(
        int customerId, Guid addressId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<CustomerAddress>(new CommandDefinition($"""
            {AddressSelect}
            WHERE a.supplier_id = @CustomerId AND a.object_id = @AddressId
            """, new { CustomerId = customerId, AddressId = addressId }, cancellationToken: cancellationToken));
    }

    public async Task<Guid> CreateAddressAsync(
        int customerId, CustomerAddressUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The first address is the default; asking for the default moves it here.
        var hasDefault = await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (SELECT 1 FROM public.supplier_address WHERE supplier_id = @CustomerId AND is_default)
            """, new { CustomerId = customerId }, transaction, cancellationToken: cancellationToken));
        var makeDefault = request.IsDefault == true || !hasDefault;
        if (makeDefault && hasDefault)
            await ClearDefaultAsync(connection, transaction, customerId, cancellationToken);

        var addressId = await connection.QuerySingleAsync<Guid>(new CommandDefinition("""
            INSERT INTO public.supplier_address
                (supplier_id, label, recipient_name, recipient_phone, address_line1, address_line2, landmark,
                 area_id, district_id, country_code, is_default)
            VALUES
                (@CustomerId, @Label, @RecipientName, @RecipientPhone, @AddressLine1, @AddressLine2, @Landmark,
                 @AreaId, @DistrictId, 'BD', @IsDefault)
            RETURNING object_id
            """, AddressParameters(customerId, request, makeDefault), transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return addressId;
    }

    public async Task<bool> UpdateAddressAsync(
        int customerId, Guid addressId, CustomerAddressUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var isDefault = await connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition("""
            SELECT is_default FROM public.supplier_address
            WHERE supplier_id = @CustomerId AND object_id = @AddressId
            FOR UPDATE
            """, new { CustomerId = customerId, AddressId = addressId }, transaction, cancellationToken: cancellationToken));
        if (isDefault is null)
            return false;

        var makeDefault = request.IsDefault ?? isDefault.Value;
        if (makeDefault && !isDefault.Value)
            await ClearDefaultAsync(connection, transaction, customerId, cancellationToken);

        var parameters = AddressParameters(customerId, request, makeDefault);
        parameters.Add("AddressId", addressId);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE public.supplier_address
            SET label = @Label,
                recipient_name = @RecipientName,
                recipient_phone = @RecipientPhone,
                address_line1 = @AddressLine1,
                address_line2 = @AddressLine2,
                landmark = @Landmark,
                area_id = @AreaId,
                district_id = @DistrictId,
                is_default = @IsDefault,
                updated_at = now()
            WHERE supplier_id = @CustomerId AND object_id = @AddressId
            """, parameters, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAddressAsync(int customerId, Guid addressId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var wasDefault = await connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition("""
            DELETE FROM public.supplier_address
            WHERE supplier_id = @CustomerId AND object_id = @AddressId
            RETURNING is_default
            """, new { CustomerId = customerId, AddressId = addressId }, transaction, cancellationToken: cancellationToken));
        if (wasDefault is null)
            return false;

        // The oldest remaining address becomes the default.
        if (wasDefault.Value)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE public.supplier_address SET is_default = true, updated_at = now()
                WHERE id = (SELECT id FROM public.supplier_address
                            WHERE supplier_id = @CustomerId AND district_id IS NOT NULL
                            ORDER BY created_at, id LIMIT 1)
                """, new { CustomerId = customerId }, transaction, cancellationToken: cancellationToken));
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<int>> GetWishlistAsync(int customerId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<int>(new CommandDefinition("""
            SELECT product_id FROM public.wishlist_item
            WHERE customer_id = @CustomerId
            ORDER BY created_at DESC, product_id
            """, new { CustomerId = customerId }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<int?> GetListingIdAsync(int organizationId, int productId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition("""
            SELECT coalesce(parent_product_id, product_id)
            FROM public.product
            WHERE product_id = @ProductId AND organization_id = @Org
            """, new { ProductId = productId, Org = organizationId }, cancellationToken: cancellationToken));
    }

    public async Task AddToWishlistAsync(int customerId, int listingId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO public.wishlist_item (customer_id, product_id) VALUES (@CustomerId, @ListingId)
            ON CONFLICT (customer_id, product_id) DO NOTHING
            """, new { CustomerId = customerId, ListingId = listingId }, cancellationToken: cancellationToken));
    }

    public async Task RemoveFromWishlistAsync(int customerId, int listingId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM public.wishlist_item WHERE customer_id = @CustomerId AND product_id = @ListingId
            """, new { CustomerId = customerId, ListingId = listingId }, cancellationToken: cancellationToken));
    }

    /// <summary>The organization's customer id for this mobile (one with an account first, then the oldest).</summary>
    internal static Task<int?> FindCustomerIdByMobileAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, int organizationId, string mobile,
        CancellationToken cancellationToken)
        => connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition("""
            SELECT supplier_id
            FROM public.supplier
            WHERE organization_id = @Org AND is_customer AND mobile = @Mobile
            ORDER BY (password_hash IS NOT NULL) DESC, supplier_id
            LIMIT 1
            """, new { Org = organizationId, Mobile = mobile }, transaction, cancellationToken: cancellationToken));

    /// <summary>
    /// A new web customer: is_customer, not a supplier, supplier_code 'WEB-' || mobile. The email is kept only
    /// when no other customer of the organization uses it. Null when the code (or another unique key) is taken.
    /// </summary>
    internal static Task<int?> InsertCustomerAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, int organizationId, string fullName, string mobile,
        string? email, string? passwordHash, bool mobileVerified, CancellationToken cancellationToken)
        => connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition($"""
            INSERT INTO public.supplier
                (organization_id, supplier_code, supplier_name, mobile, email, is_supplier, is_customer, is_active,
                 password_hash, registered_at, mobile_verified_at, created_at)
            SELECT @Org, @Code, @FullName, @Mobile,
                   CASE WHEN CAST(@Email AS text) IS NULL OR EXISTS (
                            SELECT 1 FROM public.supplier s
                            WHERE s.organization_id = @Org AND s.is_customer AND lower(s.email) = lower(@Email))
                        THEN NULL ELSE @Email END,
                   false, true, true,
                   @PasswordHash,
                   CASE WHEN CAST(@PasswordHash AS text) IS NULL THEN NULL ELSE {SecurityDb.NowUtc} END,
                   CASE WHEN @MobileVerified THEN {SecurityDb.NowUtc} END,
                   {SecurityDb.NowUtc}
            ON CONFLICT DO NOTHING
            RETURNING supplier_id
            """, new
        {
            Org = organizationId,
            Code = ThinkCartRules.WebCustomerCodePrefix + mobile,
            FullName = fullName,
            Mobile = mobile,
            Email = email,
            PasswordHash = passwordHash,
            MobileVerified = mobileVerified
        }, transaction, cancellationToken: cancellationToken));

    private static Task<DateTime> InsertTokenAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, int customerId, string tokenHash, TimeSpan lifetime,
        string? createdByIp, CancellationToken cancellationToken)
        => connection.QuerySingleAsync<DateTime>(new CommandDefinition("""
            INSERT INTO public.ecom_customer_token (customer_id, token_hash, expires_at, created_by_ip)
            VALUES (@CustomerId, @TokenHash, now() + make_interval(secs => @Seconds), @CreatedByIp)
            RETURNING expires_at
            """, new
        {
            CustomerId = customerId,
            TokenHash = tokenHash,
            Seconds = lifetime.TotalSeconds,
            CreatedByIp = createdByIp
        }, transaction, cancellationToken: cancellationToken));

    private static Task ClearDefaultAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int customerId, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition("""
            UPDATE public.supplier_address SET is_default = false, updated_at = now()
            WHERE supplier_id = @CustomerId AND is_default
            """, new { CustomerId = customerId }, transaction, cancellationToken: cancellationToken));

    private static DynamicParameters AddressParameters(
        int customerId, CustomerAddressUpsertRequest request, bool isDefault)
    {
        var parameters = new DynamicParameters();
        parameters.Add("CustomerId", customerId);
        parameters.Add("Label", request.Label);
        parameters.Add("RecipientName", request.RecipientName);
        parameters.Add("RecipientPhone", request.RecipientPhone);
        parameters.Add("AddressLine1", request.AddressLine1);
        parameters.Add("AddressLine2", request.AddressLine2);
        parameters.Add("Landmark", request.Landmark);
        parameters.Add("AreaId", request.AreaId, System.Data.DbType.Int32);
        parameters.Add("DistrictId", request.DistrictId, System.Data.DbType.Int16);
        parameters.Add("IsDefault", isDefault);
        return parameters;
    }

    private sealed class OtpRow
    {
        public long Id { get; set; }
        public string CodeHash { get; set; } = string.Empty;
        public int Attempts { get; set; }
    }
}
