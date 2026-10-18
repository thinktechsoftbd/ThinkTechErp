namespace ThinkTechErpAPI.Security;

public static class SecurityPolicies
{
    /// <summary>SECURITY.V or SECURITY.M: read users, roles, permissions, menus and organizations.</summary>
    public const string View = "SecurityView";

    /// <summary>SECURITY.M: change them.</summary>
    public const string Manage = "SecurityManage";

    /// <summary>Per-IP limit for the anonymous sign-in endpoints (login, refresh, password reset).</summary>
    public const string AuthRateLimit = "SecurityAuthRateLimit";

    public const string PermissionClaim = "permission";
    public const string RoleClaim = "role";
}
