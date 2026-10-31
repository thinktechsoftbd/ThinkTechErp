using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpAPI.Security;

public sealed record IntegrationTokenSettings(string Audience, TimeSpan AccessTokenLifetime);

/// <summary>
/// Issues HS256 access tokens for integration clients: the client ID (sub and client_id) and its
/// scopes, for the integration audience only. There is no refresh token; the client asks again with
/// its credentials.
/// </summary>
public sealed class IntegrationTokenIssuer(JwtSettings jwtSettings, IntegrationTokenSettings settings)
{
    private static readonly JsonWebTokenHandler TokenHandler = new();

    private readonly SigningCredentials signingCredentials = new(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SigningKey)), SecurityAlgorithms.HmacSha256);

    public IntegrationTokenResponse Issue(IntegrationClient client)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, client.ClientId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(IntegrationPolicies.ClientIdClaim, client.ClientId)
        };
        claims.AddRange(client.Scopes.Select(scope => new Claim(IntegrationPolicies.ScopeClaim, scope)));

        var token = TokenHandler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwtSettings.Issuer,
            Audience = settings.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(settings.AccessTokenLifetime),
            SigningCredentials = signingCredentials
        });
        return new IntegrationTokenResponse
        {
            AccessToken = token,
            ExpiresIn = (int)settings.AccessTokenLifetime.TotalSeconds
        };
    }
}
