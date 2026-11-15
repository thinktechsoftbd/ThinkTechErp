using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.Settings;

public static class DistrictRules
{
    public const string ViewDistrictsPermission = "DISTRICT.V";
    public const string ManageDistrictsPermission = "DISTRICT.M";

    public const int NameMaxLength = 150;
}

/// <summary>A row of public.district with its division and shipping zone names.</summary>
public sealed class District
{
    public short Id { get; set; }
    public Guid ObjectId { get; set; }
    public short DivisionId { get; set; }
    public string DivisionName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameBn { get; set; }
    public short? ShippingZoneId { get; set; }
    public string? ShippingZoneName { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Areas (upazilas, thanas, city areas) in the district.</summary>
    public int AreaCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}

public sealed class DistrictUpsertRequest
{
    [Range(1, short.MaxValue)]
    public short DivisionId { get; set; }

    [Required, StringLength(DistrictRules.NameMaxLength, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(DistrictRules.NameMaxLength)]
    public string? NameBn { get; set; }

    /// <summary>The district's delivery zone; its areas use it unless they override it.</summary>
    [Range(1, short.MaxValue)]
    public short? ShippingZoneId { get; set; }

    public bool IsActive { get; set; } = true;
}
