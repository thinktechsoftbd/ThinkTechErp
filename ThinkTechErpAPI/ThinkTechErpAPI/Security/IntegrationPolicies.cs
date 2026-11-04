using ThinkTechErpDomain.Models.Security;

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

    /// <summary>A client with the sms.usage scope: read and use the SMS balance of its own organizations.</summary>
    public const string SmsUsage = "IntegrationSmsUsage";

    /// <summary>
    /// A client with the thinktech-website.exchange scope (the ThinkTech website): read the public
    /// catalogue, such as the active subscription modules. It changes nothing.
    /// </summary>
    public const string WebsiteExchange = "IntegrationWebsiteExchange";

    /// <summary>Per-IP limit for the integration endpoints that need a token.</summary>
    public const string RateLimit = "IntegrationRateLimit";

    public const string ClientIdClaim = "client_id";
    public const string ScopeClaim = "scope";
}

public static class IntegrationScopes
{
    public const string OrganizationSync = OrganizationClientScopeRules.OrganizationSync;
    public const string SmsUsage = OrganizationClientScopeRules.SmsUsage;
    public const string WebsiteExchange = OrganizationClientScopeRules.WebsiteExchange;

    public static readonly IReadOnlySet<string> All = OrganizationClientScopeRules.All;

    /// <summary>
    /// Scopes that act on organizations attributed to an ActingUserId. Runtime endpoints for these
    /// scopes require an ActingUserId even though the client record itself may omit it.
    /// </summary>
    public static readonly IReadOnlySet<string> OwnOrganizations = OrganizationClientScopeRules.OwnOrganizations;
}
