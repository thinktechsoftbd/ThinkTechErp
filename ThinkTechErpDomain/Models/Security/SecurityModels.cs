using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.Security;

public static class SecurityRules
{
    public const string UserNamePattern = "^[A-Za-z0-9._@-]{3,64}$";
    public const string RoleNamePattern = "^[a-z][a-z0-9_]{1,63}$";
    public const string PermissionPattern = @"^[a-z0-9_]+(\.[a-z0-9_]+)*$";
    public const string EmailPattern = @"^[^\s@]+@[^\s@]+\.[^\s@]+$";
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;

    /// <summary>Full access to every module; passes every module's admin policy.</summary>
    public const string SuperAdminRole = "admin";
    public const string SecurityAdminRole = "security_admin";
    public const string SmsClientRole = "sms_client";
}

public sealed class SecurityUser
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; }
    public string[] Roles { get; set; } = [];
    public string? ClientId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}

/// <summary>A user with the sign-in fields that never leave the API.</summary>
public sealed class SecurityUserCredentials
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? ClientId { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public string[] Roles { get; set; } = [];
}

public sealed class SecurityRole
{
    public Guid RoleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string[] Permissions { get; set; } = [];
    public int UserCount { get; set; }
    public bool IsSystem { get; set; }
}

public sealed class LoginRequest
{
    [Required, StringLength(256, MinimumLength = 1)]
    public string UserName { get; set; } = string.Empty;

    [Required, StringLength(SecurityRules.MaxPasswordLength, MinimumLength = 1)]
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class UserCreateRequest
{
    [Required, RegularExpression(SecurityRules.UserNamePattern,
        ErrorMessage = "3–64 characters: letters, numbers, dots, dashes, underscores or @.")]
    public string UserName { get; set; } = string.Empty;

    [StringLength(256), RegularExpression(SecurityRules.EmailPattern, ErrorMessage = "Enter a valid email address.")]
    public string? Email { get; set; }

    [StringLength(200)]
    public string? DisplayName { get; set; }

    [Required, StringLength(SecurityRules.MaxPasswordLength, MinimumLength = SecurityRules.MinPasswordLength)]
    public string Password { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public string[] Roles { get; set; } = [];

    /// <summary>SMS client identifier for users with the sms_client role (emitted as the client_id claim).</summary>
    [StringLength(128)]
    public string? ClientId { get; set; }
}

public sealed class UserUpdateRequest
{
    [StringLength(256), RegularExpression(SecurityRules.EmailPattern, ErrorMessage = "Enter a valid email address.")]
    public string? Email { get; set; }

    [StringLength(200)]
    public string? DisplayName { get; set; }

    public bool IsActive { get; set; }

    public string[] Roles { get; set; } = [];

    /// <summary>Omitted or null keeps the current value; an empty string clears it.</summary>
    [StringLength(128)]
    public string? ClientId { get; set; }
}

public sealed class PasswordResetRequest
{
    [Required, StringLength(SecurityRules.MaxPasswordLength, MinimumLength = SecurityRules.MinPasswordLength)]
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class RoleUpsertRequest
{
    [Required, RegularExpression(SecurityRules.RoleNamePattern,
        ErrorMessage = "Lowercase letters, numbers and underscores, starting with a letter (e.g. sms_admin).")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    public string[] Permissions { get; set; } = [];
}
