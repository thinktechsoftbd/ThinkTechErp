using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpService.Security;

/// <summary>Creates signed access tokens; implemented by the API, which owns the JWT settings.</summary>
public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(CurrentUser user);
}

public sealed record IssuedAccessToken(string Token, DateTime ExpiresAt);

public sealed record SecurityTokenSettings(TimeSpan RefreshTokenLifetime, TimeSpan PasswordResetTokenLifetime);
