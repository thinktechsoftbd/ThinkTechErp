using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.Settings;

public static class AreaRules
{
    public const string ViewAreasPermission = "AREA.V";
    public const string ManageAreasPermission = "AREA.M";

    public const int NameMaxLength = 150;

    /// <summary>The values public.area.area_type allows.</summary>
    public static readonly IReadOnlyList<string> AreaTypes = ["upazila", "thana", "city_area"];
}

/// <summary>A row of public.area with its district, division and shipping zone names.</summary>
public sealed class Area
{
    public int Id { get; set; }
    public Guid ObjectId { get; set; }
    public short DistrictId { get; set; }
    public string DistrictName { get; set; } = string.Empty;
    public short DivisionId { get; set; }
    public string DivisionName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameBn { get; set; }
    public string AreaType { get; set; } = "upazila";
    public short? ShippingZoneId { get; set; }
    public string? ShippingZoneName { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}

public sealed class AreaUpsertRequest
{
    [Range(1, short.MaxValue)]
    public short DistrictId { get; set; }

    [Required, StringLength(AreaRules.NameMaxLength, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(AreaRules.NameMaxLength)]
    public string? NameBn { get; set; }

    /// <summary>upazila, thana or city_area (see <see cref="AreaRules.AreaTypes"/>).</summary>
    [Required, StringLength(20)]
    public string AreaType { get; set; } = "upazila";

    /// <summary>Overrides the district's zone; null keeps the district's zone.</summary>
    [Range(1, short.MaxValue)]
    public short? ShippingZoneId { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>A district for the Area screen's picker and filter.</summary>
public sealed class DistrictOption
{
    public short Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DivisionName { get; set; } = string.Empty;
    public short? ShippingZoneId { get; set; }
    public string? ShippingZoneName { get; set; }
}

public sealed class ShippingZoneOption
{
    public short Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
