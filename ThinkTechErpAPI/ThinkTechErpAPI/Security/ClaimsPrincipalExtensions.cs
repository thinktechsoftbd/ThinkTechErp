using System.Globalization;
using System.Security.Claims;

namespace ThinkTechErpAPI.Security;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Reads the signed-in user's ID from the token's sub claim.</summary>
    public static bool TryGetUserId(this ClaimsPrincipal principal, out int userId)
        => int.TryParse(principal.FindFirst("sub")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out userId);
}
