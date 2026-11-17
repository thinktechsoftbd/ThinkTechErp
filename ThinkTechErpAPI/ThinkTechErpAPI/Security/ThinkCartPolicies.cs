namespace ThinkTechErpAPI.Security;

/// <summary>
/// The ThinkCart storefront API (api/thinkcart). The Next.js storefront server calls it with an integration
/// token (scope thinkcart.storefront); signed-in customers' calls carry a customer token instead, issued by
/// api/thinkcart/auth for its own audience (Authentication:ThinkCart:Audience).
/// </summary>
public static class ThinkCartPolicies
{
    /// <summary>Authentication scheme for customer tokens. User and integration tokens are rejected by it, and the reverse.</summary>
    public const string CustomerScheme = "ThinkCartCustomer";

    /// <summary>An integration token with the thinkcart.storefront scope, or a customer token.</summary>
    public const string Storefront = "ThinkCartStorefront";

    /// <summary>A customer token only (api/thinkcart/account).</summary>
    public const string Customer = "ThinkCartCustomerAccount";

    /// <summary>Marks a customer token; a token without it never passes the customer checks.</summary>
    public const string TokenUseClaim = "token_use";
    public const string CustomerTokenUse = "thinkcart_customer";

    /// <summary>organization_id of the customer's organization.</summary>
    public const string OrganizationClaim = "org";

    /// <summary>The customer's supplier.object_id (sub is supplier_id).</summary>
    public const string CustomerObjectIdClaim = "cid";
}
