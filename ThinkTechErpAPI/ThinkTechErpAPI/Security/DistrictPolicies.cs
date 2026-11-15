namespace ThinkTechErpAPI.Security;

public static class DistrictPolicies
{
    /// <summary>DISTRICT.V or DISTRICT.M: read districts and the divisions and shipping zones they use.</summary>
    public const string View = "DistrictView";

    /// <summary>DISTRICT.M: add, edit and delete districts.</summary>
    public const string Manage = "DistrictManage";
}
