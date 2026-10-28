namespace ThinkTechErpAPI.Security;

public static class SettingsPolicies
{
    /// <summary>SETTING.V or SETTING.M: read lookup tables and lookup data.</summary>
    public const string View = "SettingsView";

    /// <summary>SETTING.M: change them.</summary>
    public const string Manage = "SettingsManage";
}
