namespace ThinkTechErpAPI.Security;

public static class SecurityPolicies
{
    /// <summary>SECURITY.V or SECURITY.M: read users, roles, permissions and menus (organizations: see OrganizationPolicies).</summary>
    public const string View = "SecurityView";

    /// <summary>SECURITY.M: change them.</summary>
    public const string Manage = "SecurityManage";

    /// <summary>Per-IP limit for the anonymous sign-in endpoints (login, refresh, password reset).</summary>
    public const string AuthRateLimit = "SecurityAuthRateLimit";

    /// <summary>Per-IP limit for the anonymous subscription-validity check.</summary>
    public const string SubscriptionValidityRateLimit = "SubscriptionValidityRateLimit";

    public const string PermissionClaim = "permission";
    public const string RoleClaim = "role";
}
