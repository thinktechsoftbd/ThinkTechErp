using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.Settings;

public static class DivisionRules
{
    public const string ViewDivisionsPermission = "DIVISION.V";
    public const string ManageDivisionsPermission = "DIVISION.M";

    public const int NameMaxLength = 150;
}

/// <summary>A row of public.division with the number of its districts.</summary>
public sealed class Division
{
    public short Id { get; set; }
    public Guid ObjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameBn { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Districts in the division; a division with districts can't be deleted.</summary>
    public int DistrictCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}

public sealed class DivisionUpsertRequest
{
    [Required, StringLength(DivisionRules.NameMaxLength, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(DivisionRules.NameMaxLength)]
    public string? NameBn { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>A division for the District screen's picker and filter.</summary>
public sealed class DivisionOption
{
    public short Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
