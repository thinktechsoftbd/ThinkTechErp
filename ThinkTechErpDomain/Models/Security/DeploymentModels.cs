using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.Security;

public static class DeploymentRules
{
    public const string ViewDeploymentsPermission = "DEPLOYMENT.V";
    public const string ManageDeploymentsPermission = "DEPLOYMENT.M";

    /// <summary>The lookup list a deployment's service type comes from (Settings → Lookup Data).</summary>
    public const string ServiceTypeLookupTable = "Service Type";

    public const int RemarksMaxLength = 1000;
    public const int DescriptionMaxLength = 4000;
    public const int MaxDescriptions = 50;
}

/// <summary>
/// A deployed service of an organization (public.deployment_info_h), with its detail lines
/// (public.deployment_info_d) in the order they were entered.
/// </summary>
public sealed class DeploymentInfo
{
    public int Id { get; set; }
    public Guid ObjectId { get; set; }
    public Guid OrganizationObjectId { get; set; }

    /// <summary>A "Service Type" lookup value.</summary>
    public long ServiceTypeId { get; set; }
    public string ServiceType { get; set; } = string.Empty;

    public bool IsActive { get; set; }
    public string? Remarks { get; set; }
    public IReadOnlyList<DeploymentInfoDetail> Details { get; set; } = [];

    public DateTime CreatedDate { get; set; }
    public int? CreatedById { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public int? UpdatedById { get; set; }
    public string? UpdatedByName { get; set; }
}

public sealed class DeploymentInfoDetail
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
}

/// <summary>A value of the "Service Type" lookup list.</summary>
public sealed class DeploymentServiceType
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

/// <summary>The organization a deployment page is about, and its deployments.</summary>
public sealed class OrganizationDeployments
{
    public DeploymentOrganization Organization { get; set; } = new();
    public IReadOnlyList<DeploymentInfo> Deployments { get; set; } = [];
}

/// <summary>An organization on the deployment screens, with how many deployments it has.</summary>
public sealed class DeploymentOrganization
{
    public Guid ObjectId { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Slug { get; set; }
    public bool IsActive { get; set; }
    public bool IsExternal { get; set; }
    public int DeploymentCount { get; set; }
    public int ActiveDeploymentCount { get; set; }
    /// <summary>When a deployment of it was last added or changed.</summary>
    public DateTime? LastChangedDate { get; set; }
}

public sealed class DeploymentInfoUpsertRequest
{
    /// <summary>A "Service Type" lookup value.</summary>
    [Range(1, long.MaxValue)]
    public long ServiceTypeId { get; set; }

    public bool IsActive { get; set; } = true;

    [StringLength(DeploymentRules.RemarksMaxLength)]
    public string? Remarks { get; set; }

    /// <summary>The detail lines in order; empty ones are dropped. Saving replaces the deployment's lines.</summary>
    [MaxLength(DeploymentRules.MaxDescriptions)]
    public List<DeploymentInfoDetailRequest> Details { get; set; } = [];
}

public sealed class DeploymentInfoDetailRequest
{
    [StringLength(DeploymentRules.DescriptionMaxLength)]
    public string? Description { get; set; }
}
