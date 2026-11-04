using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.Security;

public sealed class OrganizationClient
{
    public int Id { get; set; }
    public Guid ObjectId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string? ClientSecret { get; set; }
    public string? SecretSha256Hash { get; set; }
    public Guid OrgObjectId { get; set; }
    public string? OrganizationName { get; set; }
    public int? ActingUserId { get; set; }
    public string? ActingUserName { get; set; }
    public string[] Scopes { get; set; } = [];
    public bool IsActive { get; set; }
    public bool HasSecretSha256Hash { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}

public sealed class OrganizationClientOrganizationOption
{
    public Guid ObjectId { get; set; }
    public string NameEn { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool IsActive { get; set; }
}

public sealed class OrganizationClientUpsertRequest
{
    [Required, StringLength(64, MinimumLength = 3), RegularExpression("^[A-Za-z0-9][A-Za-z0-9_]*$")]
    public string ClientName { get; set; } = string.Empty;

    [Required, StringLength(512, MinimumLength = 1)]
    public string ClientSecret { get; set; } = string.Empty;

    [Required, RegularExpression("^[0-9a-fA-F]{64}$")]
    public string SecretSha256Hash { get; set; } = string.Empty;

    [Required]
    public Guid? OrgObjectId { get; set; }

    [Range(1, int.MaxValue)]
    public int? ActingUserId { get; set; }

    [Required, MinLength(1), MaxLength(50)]
    public string[] Scopes { get; set; } = [];

    public bool IsActive { get; set; } = true;
}

/// <summary>Internal credential projection used only by integration authentication.</summary>
public sealed class OrganizationClientCredentials
{
    public string ClientName { get; set; } = string.Empty;
    public string SecretSha256Hash { get; set; } = string.Empty;
    public int? ActingUserId { get; set; }
    public string[] Scopes { get; set; } = [];
    public bool IsActive { get; set; }
}

public static class OrganizationClientScopeRules
{
    public const string OrganizationSync = "organization.sync";
    public const string SmsUsage = "sms.usage";
    public const string WebsiteExchange = "thinktech-website.exchange";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        OrganizationSync, SmsUsage, WebsiteExchange
    };

    public static readonly IReadOnlySet<string> OwnOrganizations = new HashSet<string>(StringComparer.Ordinal)
    {
        OrganizationSync, SmsUsage
    };
}

public sealed class OrganizationClientActiveRequest
{
    public bool IsActive { get; set; }
}
