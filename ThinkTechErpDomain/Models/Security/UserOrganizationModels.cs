using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.Security;

/// <summary>An organization the signed-in user can work in (inventory, purchase, ...).</summary>
public sealed class UserOrganization
{
    public int OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool IsDefault { get; set; }
    /// <summary>Linked to the user in security.user_organization (not only reachable as Super Admin).</summary>
    public bool IsLinked { get; set; }
    /// <summary>Outside this company (e.g. a customer); the ERP's organization switcher leaves these out.</summary>
    public bool IsExternal { get; set; }
}

public sealed class UserOrganizationsUpdateRequest
{
    /// <summary>Every organization the user works in.</summary>
    public List<int> OrganizationIds { get; set; } = [];

    [Range(1, int.MaxValue)]
    public int? DefaultOrganizationId { get; set; }
}
