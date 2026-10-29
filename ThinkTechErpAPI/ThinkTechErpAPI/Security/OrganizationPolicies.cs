namespace ThinkTechErpAPI.Security;

public static class OrganizationPolicies
{
    /// <summary>
    /// ORGANIZATION.V or ORGANIZATION.M: read organizations. SECURITY.V and SECURITY.M also
    /// qualify, because the subscription screens list organizations to choose from.
    /// </summary>
    public const string View = "OrganizationView";

    /// <summary>ORGANIZATION.M: add and edit organizations, including the Organization Id.</summary>
    public const string Manage = "OrganizationManage";
}
