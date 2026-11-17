using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ThinkTechErpService.Security;
using ThinkTechErpService.ThinkCart;

namespace ThinkTechErpAPI.Security;

/// <summary>
/// Issues HS256 access tokens for storefront customers: sub (supplier_id), jti, org (organization_id), cid
/// (supplier object_id) and token_use, for the ThinkCart audience only. They are renewed with the rotating
/// refresh token from api/thinkcart/auth/refresh.
/// </summary>
public sealed class ThinkCartCustomerTokenIssuer(JwtSettings jwtSettings, ThinkCartTokenSettings settings)
    : IThinkCartCustomerTokenIssuer
{
    private static readonly JsonWebTokenHandler TokenHandler = new();

    private readonly SigningCredentials signingCredentials = new(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SigningKey)), SecurityAlgorithms.HmacSha256);

    public IssuedAccessToken Issue(ThinkCartCustomerIdentity customer)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now.Add(settings.AccessTokenLifetime);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, customer.CustomerId.ToString(CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ThinkCartPolicies.OrganizationClaim, customer.OrganizationId.ToString(CultureInfo.InvariantCulture)),
            new(ThinkCartPolicies.CustomerObjectIdClaim, customer.ObjectId.ToString()),
            new(ThinkCartPolicies.TokenUseClaim, ThinkCartPolicies.CustomerTokenUse)
        };

        var token = TokenHandler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwtSettings.Issuer,
            Audience = settings.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresAt,
            SigningCredentials = signingCredentials
        });
        return new IssuedAccessToken(token, expiresAt);
    }
}
