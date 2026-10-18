using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpService.Security;

/// <summary>
/// Placeholder until email or SMS delivery exists. In Development the reset token is written to the
/// log so the flow can be tested; elsewhere only a warning is logged. Register another
/// <see cref="IPasswordResetNotifier"/> to replace it.
/// </summary>
public sealed class LoggingPasswordResetNotifier(
    IHostEnvironment environment,
    ILogger<LoggingPasswordResetNotifier> logger) : IPasswordResetNotifier
{
    public Task SendAsync(
        SecurityUserCredentials user, string resetToken, TimeSpan validFor, CancellationToken cancellationToken = default)
    {
        if (environment.IsDevelopment())
        {
            logger.LogInformation(
                "Password reset token for user {UserId}: {ResetToken} (valid for {Minutes} minutes)",
                user.Id, resetToken, validFor.TotalMinutes);
        }
        else
        {
            logger.LogWarning(
                "Password reset requested for user {UserId}, but no delivery channel is configured.", user.Id);
        }

        return Task.CompletedTask;
    }
}
