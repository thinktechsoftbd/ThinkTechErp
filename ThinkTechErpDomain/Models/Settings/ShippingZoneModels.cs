using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.Settings;

public static class ShippingZoneRules
{
    public const string ViewShippingZonesPermission = "SHIPPING_ZONE.V";
    public const string ManageShippingZonesPermission = "SHIPPING_ZONE.M";

    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 150;
    public const int MaxDeliveryDays = 365;

    /// <summary>Lowercase words joined by underscores, like the existing inside_dhaka and outside_dhaka.</summary>
    public const string CodePattern = "^[a-z0-9]+(_[a-z0-9]+)*$";
}

/// <summary>A row of public.shipping_zone with the number of districts and areas in it.</summary>
public sealed class ShippingZone
{
    public short Id { get; set; }
    public Guid ObjectId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public short? MinDeliveryDays { get; set; }
    public short? MaxDeliveryDays { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Districts whose zone this is.</summary>
    public int DistrictCount { get; set; }

    /// <summary>Areas that override their district's zone with this one.</summary>
    public int AreaCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ShippingZoneUpsertRequest
{
    [Required, StringLength(ShippingZoneRules.CodeMaxLength, MinimumLength = 1),
     RegularExpression(ShippingZoneRules.CodePattern,
         ErrorMessage = "Use lowercase letters, digits and underscores, e.g. inside_dhaka.")]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(ShippingZoneRules.NameMaxLength, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Range(0, ShippingZoneRules.MaxDeliveryDays)]
    public short? MinDeliveryDays { get; set; }

    [Range(0, ShippingZoneRules.MaxDeliveryDays)]
    public short? MaxDeliveryDays { get; set; }

    public bool IsActive { get; set; } = true;
}
