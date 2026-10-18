using System.Text;
using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpService.Security;

internal static class PasswordPolicy
{
    public static void Validate(string password)
    {
        if (password.Length < SecurityRules.MinPasswordLength)
            throw new SecurityValidationException(
                $"Password must be at least {SecurityRules.MinPasswordLength} characters.");

        if (Encoding.UTF8.GetByteCount(password) > SecurityRules.MaxPasswordBytes)
            throw new SecurityValidationException(
                $"Password must be at most {SecurityRules.MaxPasswordBytes} bytes (72 plain letters, digits or symbols).");
    }
}
