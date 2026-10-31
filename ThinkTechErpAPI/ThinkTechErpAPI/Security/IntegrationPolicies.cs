namespace ThinkTechErpAPI.Security;

public static class IntegrationPolicies
{
    /// <summary>
    /// Authentication scheme for server-to-server clients (Authentication:Integration). Their tokens
    /// carry their own audience, so they are rejected by every other endpoint, and user tokens are
    /// rejected here.
    /// </summary>
    public const string Scheme = "IntegrationBearer";

    /// <summary>A client with the organization.sync scope: create and update its own organizations.</summary>
    public const string OrganizationSync = "IntegrationOrganizationSync";

    /// <summary>Per-IP limit for the integration endpoints that need a token.</summary>
    public const string RateLimit = "IntegrationRateLimit";

    public const string ClientIdClaim = "client_id";
    public const string ScopeClaim = "scope";
}

public static class IntegrationScopes
{
    public const string OrganizationSync = "organization.sync";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { OrganizationSync };
}
