using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpService.Security;

namespace ThinkTechErpAPI.Security;

public sealed record JwtSettings(string Issuer, string Audience, string SigningKey, TimeSpan AccessTokenLifetime);

/// <summary>
/// Issues HS256 access tokens carrying the user's ID (sub), roles and permission codes, matching the
/// validation parameters in Program.cs.
/// </summary>
public sealed class JwtAccessTokenIssuer(JwtSettings settings) : IAccessTokenIssuer
{
    private static readonly JsonWebTokenHandler TokenHandler = new();

    private readonly SigningCredentials signingCredentials = new(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)), SecurityAlgorithms.HmacSha256);

    public IssuedAccessToken Issue(CurrentUser user)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now.Add(settings.AccessTokenLifetime);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.Name)
        };
        claims.AddRange(user.Roles.Select(role => new Claim(SecurityPolicies.RoleClaim, role)));
        claims.AddRange(user.Permissions.Select(permission => new Claim(SecurityPolicies.PermissionClaim, permission)));

        var token = TokenHandler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
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
