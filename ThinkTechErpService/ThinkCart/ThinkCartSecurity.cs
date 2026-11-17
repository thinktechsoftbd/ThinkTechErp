using Microsoft.Extensions.Caching.Memory;
using ThinkTechErpRepository.ThinkCart;
using ThinkTechErpService.Security;

namespace ThinkTechErpService.ThinkCart;

/// <summary>Customer tokens (Authentication:ThinkCart): their audience and lifetimes.</summary>
public sealed record ThinkCartTokenSettings(string Audience, TimeSpan AccessTokenLifetime, TimeSpan RefreshTokenLifetime);

/// <summary>Who a customer access token is for: sub, org and cid.</summary>
public sealed record ThinkCartCustomerIdentity(int CustomerId, Guid ObjectId, int OrganizationId);

/// <summary>Creates signed customer access tokens; implemented by the API, which owns the signing key.</summary>
public interface IThinkCartCustomerTokenIssuer
{
    IssuedAccessToken Issue(ThinkCartCustomerIdentity customer);
}

/// <summary>The organization a storefront integration client serves (security_organization_client.org_object_id).</summary>
public interface IThinkCartOrganizationResolver
{
    /// <returns>The organization_id, or null when the client has no active organization.</returns>
    Task<int?> ResolveClientOrganizationAsync(string clientName, CancellationToken cancellationToken = default);
}

/// <summary>Looks the client's organization up and keeps it for five minutes, so most requests skip the query.</summary>
public sealed class ThinkCartOrganizationResolver(IThinkCartCustomerRepository customers, IMemoryCache cache)
    : IThinkCartOrganizationResolver
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    public async Task<int?> ResolveClientOrganizationAsync(string clientName, CancellationToken cancellationToken = default)
    {
        var key = "thinkcart:client-organization:" + clientName;
        if (cache.TryGetValue(key, out int organizationId))
            return organizationId;

        var resolved = await customers.GetClientOrganizationIdAsync(clientName, cancellationToken);
        if (resolved is int id)
            cache.Set(key, id, CacheLifetime);
        return resolved;
    }
}
