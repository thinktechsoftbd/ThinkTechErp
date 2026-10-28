using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.Settings;

public static class SettingsRules
{
    public const string ViewSettingsPermission = "SETTING.V";
    public const string ManageSettingsPermission = "SETTING.M";

    /// <summary>The lookup table whose values are the currencies a subscription can be billed in.</summary>
    public const string CurrencyLookupTable = "Currency";
}

public sealed class LookupTable
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int DataCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}

public sealed class LookupTableUpsertRequest
{
    [Required, StringLength(150, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public sealed class LookupData
{
    public long Id { get; set; }
    public long LookupTableId { get; set; }
    public string LookupTableName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}

public sealed class LookupDataUpsertRequest
{
    [Range(1, long.MaxValue)]
    public long LookupTableId { get; set; }

    [Required, StringLength(150, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

/// <summary>An active value of an active lookup table, for drop-downs.</summary>
public sealed class LookupOption
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
