namespace ThinkTechErpAPI.Security;

public static class SmsPolicies
{
    /// <summary>SMS.V or SMS.M: read providers, purchases, allocations and organization balances.</summary>
    public const string View = "SmsView";

    /// <summary>SMS.M: change providers, purchases and allocations; record organization usage.</summary>
    public const string Manage = "SmsManage";
}
