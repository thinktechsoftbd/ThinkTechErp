using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpService.Security;

/// <summary>Delivers a password-reset token to its owner (email, SMS, ...).</summary>
public interface IPasswordResetNotifier
{
    Task SendAsync(SecurityUserCredentials user, string resetToken, TimeSpan validFor, CancellationToken cancellationToken = default);
}
