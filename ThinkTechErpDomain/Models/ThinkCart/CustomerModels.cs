using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.ThinkCart;

public sealed class CustomerProfile
{
    /// <summary>supplier.object_id.</summary>
    public Guid CustomerId { get; set; }

    public string FullName { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateTime? RegisteredAt { get; set; }
}

public sealed class CustomerAuthResponse
{
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Seconds until the access token expires.</summary>
    public int ExpiresIn { get; set; }

    public string RefreshToken { get; set; } = string.Empty;
    public DateTime RefreshTokenExpiresAt { get; set; }
    public CustomerProfile Customer { get; set; } = new();
}

public sealed class CustomerRegisterRequest
{
    [Required, StringLength(ThinkCartRules.NameMaxLength, MinimumLength = 1)]
    public string FullName { get; set; } = string.Empty;

    [Required, StringLength(30)]
    public string Mobile { get; set; } = string.Empty;

    [StringLength(ThinkCartRules.EmailMaxLength)]
    public string? Email { get; set; }

    [Required, StringLength(ThinkCartRules.MaxPasswordLength)]
    public string Password { get; set; } = string.Empty;

    [StringLength(ThinkCartRules.OtpLength)]
    public string? OtpCode { get; set; }
}

public sealed class CustomerLoginRequest
{
    /// <summary>Mobile number or email.</summary>
    [Required, StringLength(ThinkCartRules.EmailMaxLength)]
    public string Login { get; set; } = string.Empty;

    [Required, StringLength(ThinkCartRules.MaxPasswordLength)]
    public string Password { get; set; } = string.Empty;
}

public sealed class OtpSendRequest
{
    [Required, StringLength(30)]
    public string Mobile { get; set; } = string.Empty;

    [Required, StringLength(20)]
    public string Purpose { get; set; } = string.Empty;
}

public sealed class OtpSendResponse
{
    public int ExpiresInSeconds { get; set; }
}

public sealed class OtpLoginRequest
{
    [Required, StringLength(30)]
    public string Mobile { get; set; } = string.Empty;

    [Required, StringLength(ThinkCartRules.OtpLength)]
    public string Code { get; set; } = string.Empty;
}

public sealed class OtpPasswordResetRequest
{
    [Required, StringLength(30)]
    public string Mobile { get; set; } = string.Empty;

    [Required, StringLength(ThinkCartRules.OtpLength)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(ThinkCartRules.MaxPasswordLength)]
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class CustomerRefreshRequest
{
    [Required, StringLength(256)]
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed class CustomerProfileUpdateRequest
{
    [Required, StringLength(ThinkCartRules.NameMaxLength, MinimumLength = 1)]
    public string FullName { get; set; } = string.Empty;

    [StringLength(ThinkCartRules.EmailMaxLength)]
    public string? Email { get; set; }

    [StringLength(10)]
    public string? Gender { get; set; }

    public DateOnly? DateOfBirth { get; set; }
}

public sealed class CustomerPasswordChangeRequest
{
    [Required, StringLength(ThinkCartRules.MaxPasswordLength)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, StringLength(ThinkCartRules.MaxPasswordLength)]
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class CustomerAddress
{
    /// <summary>supplier_address.object_id.</summary>
    public Guid AddressId { get; set; }

    public string? Label { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string RecipientPhone { get; set; } = string.Empty;
    public short DistrictId { get; set; }
    public string DistrictName { get; set; } = string.Empty;
    public int? AreaId { get; set; }
    public string? AreaName { get; set; }
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string? Landmark { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class CustomerAddressUpsertRequest
{
    [StringLength(50)]
    public string? Label { get; set; }

    [Required, StringLength(ThinkCartRules.NameMaxLength, MinimumLength = 1)]
    public string RecipientName { get; set; } = string.Empty;

    [Required, StringLength(30)]
    public string RecipientPhone { get; set; } = string.Empty;

    [Range(1, short.MaxValue)]
    public short DistrictId { get; set; }

    [Range(1, int.MaxValue)]
    public int? AreaId { get; set; }

    [Required, StringLength(ThinkCartRules.AddressMaxLength, MinimumLength = 1)]
    public string AddressLine1 { get; set; } = string.Empty;

    [StringLength(ThinkCartRules.AddressMaxLength)]
    public string? AddressLine2 { get; set; }

    [StringLength(ThinkCartRules.AddressMaxLength)]
    public string? Landmark { get; set; }

    public bool? IsDefault { get; set; }
}

/// <summary>A web customer: a supplier row with is_customer = true.</summary>
public sealed class CustomerRecord
{
    public int Id { get; set; }
    public Guid ObjectId { get; set; }
    public int OrganizationId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateTime? RegisteredAt { get; set; }
    public string? PasswordHash { get; set; }
    public bool IsActive { get; set; }
    public bool IsBlocked { get; set; }

    /// <summary>A guest ordered without an account: no password yet.</summary>
    public bool IsGuest => string.IsNullOrEmpty(PasswordHash);

    public CustomerProfile ToProfile() => new()
    {
        CustomerId = ObjectId,
        FullName = FullName,
        Mobile = Mobile ?? string.Empty,
        Email = Email,
        Gender = Gender,
        DateOfBirth = DateOfBirth,
        RegisteredAt = RegisteredAt
    };
}

public sealed class CustomerTokenRecord
{
    public long Id { get; set; }
    public int CustomerId { get; set; }
    public bool IsExpired { get; set; }
    public bool IsRevoked { get; set; }
    public bool WasJustRotated { get; set; }
}

public enum OtpCheckResult
{
    Valid,
    Invalid,
    TooManyAttempts
}

public enum OtpSendStatus
{
    Sent,
    TooSoon,
    TooMany,
    NoAccount,
    AlreadyRegistered
}

public sealed record OtpSendResult(OtpSendStatus Status, int ExpiresInSeconds = 0, int RetryAfterSeconds = 0);

public enum CustomerAuthStatus
{
    Succeeded,
    InvalidCredentials,
    Blocked
}

public sealed record CustomerAuthResult(CustomerAuthStatus Status, CustomerAuthResponse? Response = null);
