using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ThinkTechErpService.ThinkCart;

/// <summary>
/// Delivers a storefront OTP code to a mobile number. The ERP has no SMS gateway yet (the SMS module only
/// tracks balances); register an implementation before AddThinkTechErpServices to plug one in.
/// </summary>
public interface IThinkCartOtpSender
{
    Task SendAsync(string mobile, string code, string purpose, TimeSpan lifetime, CancellationToken cancellationToken = default);
}

/// <summary>
/// The default sender: no SMS goes out. In Development the code is written to the log so the storefront
/// can be tested; elsewhere only a warning (without the code) is logged.
/// </summary>
public sealed class LoggingThinkCartOtpSender(IHostEnvironment environment, ILogger<LoggingThinkCartOtpSender> logger)
    : IThinkCartOtpSender
{
    public Task SendAsync(string mobile, string code, string purpose, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        if (environment.IsDevelopment())
        {
            logger.LogInformation(
                "ThinkCart OTP for {Mobile} ({Purpose}): {Code}, valid for {Minutes} minutes. No SMS gateway is configured.",
                mobile, purpose, code, (int)lifetime.TotalMinutes);
        }
        else
        {
            logger.LogWarning(
                "No SMS gateway is configured: the ThinkCart {Purpose} code for {Mobile} was not delivered.",
                purpose, Mask(mobile));
        }

        return Task.CompletedTask;
    }

    private static string Mask(string mobile) => mobile.Length > 4 ? new string('*', mobile.Length - 3) + mobile[^3..] : "***";
}
