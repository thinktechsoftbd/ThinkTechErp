namespace ThinkTechErpAPI.Security;

public static class AreaPolicies
{
    /// <summary>AREA.V or AREA.M: read areas, districts and shipping zones.</summary>
    public const string View = "AreaView";

    /// <summary>AREA.M: add, edit and delete areas.</summary>
    public const string Manage = "AreaManage";
}
