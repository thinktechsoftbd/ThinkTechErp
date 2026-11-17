namespace ThinkTechErpAPI.Security;

public static class WarrantyPolicies
{
    /// <summary>WARRANTY.V or WARRANTY.M: read warranty policies, cards, service jobs and supplier claims.</summary>
    public const string View = "WarrantyView";

    /// <summary>WARRANTY.M: set policies, issue and void cards, work and bill service jobs, send supplier claims.</summary>
    public const string Manage = "WarrantyManage";
}
