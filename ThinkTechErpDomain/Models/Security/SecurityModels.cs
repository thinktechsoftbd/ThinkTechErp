using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.Security;

public static class SecurityRules
{
    public const string EmailPattern = @"^[^\s@]+@[^\s@]+\.[^\s@]+$";
    public const string PermissionCodePattern = @"^[A-Z][A-Z0-9_]*(\.[A-Z0-9_]+)*$";
    public const string UserStatusPattern = "^(ACTIVE|INACTIVE)$";
    public const int MinPasswordLength = 8;

    /// <summary>bcrypt only reads the first 72 bytes, so longer passwords are rejected instead of silently truncated.</summary>
    public const int MaxPasswordBytes = 72;

    public const string ViewSecurityPermission = "SECURITY.V";
    public const string ManageSecurityPermission = "SECURITY.M";

    public const string ViewOrganizationPermission = "ORGANIZATION.V";
    public const string ManageOrganizationPermission = "ORGANIZATION.M";
}

public static class UserStatuses
{
    public const string Active = "ACTIVE";
    public const string Inactive = "INACTIVE";
}

public enum DeleteOutcome
{
    Deleted,
    NotFound,
    InUse
}

public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Limit { get; set; }
    public int Offset { get; set; }
}

// ---- Authentication

public sealed class LoginRequest
{
    [Required, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(256)]
    public string Password { get; set; } = string.Empty;
}

public sealed class RefreshTokenRequest
{
    [Required, StringLength(256)]
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    [Required, StringLength(256)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, StringLength(256, MinimumLength = SecurityRules.MinPasswordLength)]
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class ForgotPasswordRequest
{
    [Required, StringLength(256), RegularExpression(SecurityRules.EmailPattern, ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    [Required, StringLength(256)]
    public string Token { get; set; } = string.Empty;

    [Required, StringLength(256, MinimumLength = SecurityRules.MinPasswordLength)]
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class CurrentUser
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string[] Roles { get; set; } = [];
    public string[] Permissions { get; set; } = [];
}

public sealed class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public DateTime AccessTokenExpiresAt { get; set; }
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime RefreshTokenExpiresAt { get; set; }
    public CurrentUser User { get; set; } = new();
}

/// <summary>A user with the sign-in fields that never leave the API.</summary>
public sealed class SecurityUserCredentials
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string Status { get; set; } = UserStatuses.Active;
}

public sealed class UserAccess
{
    public string[] Roles { get; set; } = [];
    public string[] Permissions { get; set; } = [];
}

public sealed class RefreshTokenRecord
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public bool IsExpired { get; set; }
    public bool IsRevoked { get; set; }

    /// <summary>Rotated moments ago, e.g. by a parallel refresh from another browser tab.</summary>
    public bool WasJustRotated { get; set; }
}

/// <summary>A menu the signed-in user may see. The Can* flags are null when no per-user override exists.</summary>
public sealed class MenuNode
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public int? ParentId { get; set; }
    public bool? CanCreate { get; set; }
    public bool? CanEdit { get; set; }
    public bool? CanDelete { get; set; }
    public List<MenuNode> Children { get; set; } = [];
}

// ---- Users

public sealed class RoleSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class SecurityUser
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string Status { get; set; } = UserStatuses.Active;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public RoleSummary[] Roles { get; set; } = [];
}

public sealed class UserCreateRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(256), RegularExpression(SecurityRules.EmailPattern, ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(256, MinimumLength = SecurityRules.MinPasswordLength)]
    public string Password { get; set; } = string.Empty;

    [StringLength(500)]
    public string? PhotoUrl { get; set; }

    [Required, RegularExpression(SecurityRules.UserStatusPattern, ErrorMessage = "Status must be ACTIVE or INACTIVE.")]
    public string Status { get; set; } = UserStatuses.Active;

    public int[] RoleIds { get; set; } = [];
}

public sealed class UserUpdateRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(256), RegularExpression(SecurityRules.EmailPattern, ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = string.Empty;

    [StringLength(500)]
    public string? PhotoUrl { get; set; }

    [Required, RegularExpression(SecurityRules.UserStatusPattern, ErrorMessage = "Status must be ACTIVE or INACTIVE.")]
    public string Status { get; set; } = UserStatuses.Active;

    /// <summary>Null keeps the current roles; an array (even empty) replaces them.</summary>
    public int[]? RoleIds { get; set; }
}

public sealed class SetPasswordRequest
{
    [Required, StringLength(256, MinimumLength = SecurityRules.MinPasswordLength)]
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class PermissionAssignmentRequest
{
    [Required]
    public int[] PermissionIds { get; set; } = [];
}

public sealed class UserPermissionSet
{
    /// <summary>Permissions granted to the user directly (UserPermission).</summary>
    public SecurityPermission[] Direct { get; set; } = [];

    /// <summary>Codes from the user's roles plus direct grants: what the access token carries.</summary>
    public string[] Effective { get; set; } = [];
}

public sealed class UserMenuAccess
{
    public int MenuId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool CanView { get; set; }
    public bool CanCreate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
}

public sealed class UserMenuAccessItem
{
    [Range(1, int.MaxValue)]
    public int MenuId { get; set; }

    public bool CanView { get; set; } = true;
    public bool CanCreate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
}

public sealed class UserMenuAssignmentRequest
{
    [Required]
    public UserMenuAccessItem[] Menus { get; set; } = [];
}

// ---- Roles and permissions

public sealed class SecurityPermission
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public sealed class PermissionUpsertRequest
{
    [Required, StringLength(100), RegularExpression(SecurityRules.PermissionCodePattern,
        ErrorMessage = "Use uppercase MODULE.ACTION, e.g. PATIENT.V.")]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(200, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }
}

public sealed class SecurityRole
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int UserCount { get; set; }
    public SecurityPermission[] Permissions { get; set; } = [];
}

public sealed class RoleUpsertRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>On update, null keeps the current permissions; an array (even empty) replaces them.</summary>
    public int[]? PermissionIds { get; set; }
}

// ---- Menus

public sealed class SecurityMenu
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public int? ParentId { get; set; }
    public int? RoleId { get; set; }
    public bool IsActive { get; set; }
}

public sealed class MenuUpsertRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Client route, or "#" for a group that only holds child menus.</summary>
    [Required, StringLength(300, MinimumLength = 1)]
    public string Path { get; set; } = string.Empty;

    [StringLength(100)]
    public string? Icon { get; set; }

    public int SortOrder { get; set; }

    [Range(1, int.MaxValue)]
    public int? ParentId { get; set; }

    /// <summary>Limits the menu to one role; null shows it to every signed-in user.</summary>
    [Range(1, int.MaxValue)]
    public int? RoleId { get; set; }

    public bool IsActive { get; set; } = true;
}

// ---- Organizations

public sealed class Organization
{
    public int Id { get; set; }

    /// <summary>
    /// Public identifier, shown as "Organization Id". Generated by the database when not given;
    /// it can be changed, but must stay unique.
    /// </summary>
    public Guid ObjectId { get; set; }

    public string NameEn { get; set; } = string.Empty;
    public string? NameBn { get; set; }
    public string? Slogan { get; set; }
    public string? Code { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public string? LogoUrl { get; set; }

    /// <summary>An organization outside this company (e.g. a customer), rather than one of its own.</summary>
    public bool IsExternal { get; set; }

    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}

/// <summary>
/// An organization's descriptive fields. Integration clients send only these;
/// OrganizationUpsertRequest adds the fields the ERP controls.
/// </summary>
public class OrganizationDetails
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string NameEn { get; set; } = string.Empty;

    [StringLength(200)]
    public string? NameBn { get; set; }

    [StringLength(500)]
    public string? Slogan { get; set; }

    [StringLength(50)]
    public string? Code { get; set; }

    [StringLength(150), RegularExpression(SecurityRules.EmailPattern, ErrorMessage = "Enter a valid email address.")]
    public string? Email { get; set; }

    [StringLength(50)]
    public string? Phone { get; set; }

    [StringLength(255)]
    public string? Website { get; set; }

    [StringLength(255)]
    public string? AddressLine1 { get; set; }

    [StringLength(255)]
    public string? AddressLine2 { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(100)]
    public string? State { get; set; }

    [StringLength(20)]
    public string? PostalCode { get; set; }

    [StringLength(100)]
    public string? Country { get; set; }

    [StringLength(500)]
    public string? LogoUrl { get; set; }
}

public sealed class OrganizationUpsertRequest : OrganizationDetails
{
    /// <summary>
    /// The Organization Id. Null keeps the current one; on create, null lets the database
    /// generate one.
    /// </summary>
    public Guid? ObjectId { get; set; }

    public bool IsExternal { get; set; }

    public bool IsActive { get; set; } = true;
}

// ---- Integration clients (server-to-server)

public sealed class IntegrationTokenRequest
{
    [Required, StringLength(64)]
    public string ClientId { get; set; } = string.Empty;

    [Required, StringLength(256)]
    public string ClientSecret { get; set; } = string.Empty;
}

public sealed class IntegrationTokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";

    /// <summary>Seconds until the access token expires. Then ask again with the client credentials.</summary>
    public int ExpiresIn { get; set; }
}

public enum ClientOrganizationSyncStatus
{
    Created,
    Updated,

    /// <summary>The Organization Id belongs to an organization another client or a user created.</summary>
    NotOwned
}

public sealed record ClientOrganizationSync(ClientOrganizationSyncStatus Status, Organization? Organization = null);
