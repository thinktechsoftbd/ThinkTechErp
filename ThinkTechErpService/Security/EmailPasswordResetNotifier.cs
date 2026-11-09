using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ThinkTechErpDomain.Models.Security;

namespace ThinkTechErpService.Security;

/// <summary>
/// Emails the user a "Reset password" button that opens the client's reset page ({PasswordReset:ResetUrl}?token=...):
/// <code>
/// "Email": { "From": "no-reply@example.com", "FromName": "ThinkTechErp",
///            "Smtp": { "Host": "smtp.example.com", "Port": 587, "UserName": "...", "Password": "...", "EnableSsl": true } },
/// "PasswordReset": { "ResetUrl": "http://localhost:3000/reset-password" }
/// </code>
/// Without Email:Smtp:Host, in Development the email is saved as an .eml file in Email:PickupDirectory
/// (default App_Data/mail under the API) to open in a mail app, and the link is logged too; elsewhere only a
/// warning is logged. A failed send is logged, since the API answers a reset request the same way whatever happens.
/// </summary>
public sealed class EmailPasswordResetNotifier(
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<EmailPasswordResetNotifier> logger) : IPasswordResetNotifier
{
    private const string DefaultResetUrl = "http://localhost:3000/reset-password";

    public async Task SendAsync(
        SecurityUserCredentials user, string resetToken, TimeSpan validFor, CancellationToken cancellationToken = default)
    {
        var resetUrl = configuration["PasswordReset:ResetUrl"];
        var link = $"{(string.IsNullOrWhiteSpace(resetUrl) ? DefaultResetUrl : resetUrl.Trim())}?token={Uri.EscapeDataString(resetToken)}";
        var minutes = (int)validFor.TotalMinutes;
        var host = configuration["Email:Smtp:Host"]?.Trim();
        var local = string.IsNullOrEmpty(host);

        if (local && !environment.IsDevelopment())
        {
            logger.LogWarning(
                "Password reset requested for user {UserId}, but Email:Smtp:Host isn't set, so nothing was sent.", user.Id);
            return;
        }

        using var message = BuildMessage(user, link, minutes);
        var folder = local ? PickupFolder() : null;
        using var client = folder is not null ? LocalClient(folder) : SmtpClientFor(host!);
        try
        {
            await client.SendMailAsync(message, cancellationToken);
            if (local)
                logger.LogInformation(
                    "Password reset email for user {UserId} ({Email}) saved as an .eml file in {Folder} (set Email:Smtp to send it). Link: {ResetLink}",
                    user.Id, user.Email, folder, link);
        }
        catch (Exception exception) when (exception is SmtpException or InvalidOperationException or IOException)
        {
            logger.LogError(exception, "The password reset email to user {UserId} couldn't be sent. Link: {ResetLink}",
                user.Id, environment.IsDevelopment() ? link : "(hidden)");
        }
    }

    private MailMessage BuildMessage(SecurityUserCredentials user, string link, int minutes)
    {
        var appName = configuration["Email:FromName"] is { Length: > 0 } fromName ? fromName : "ThinkTechErp";
        var from = configuration["Email:From"] is { Length: > 0 } address ? address.Trim() : "no-reply@localhost";
        var href = WebUtility.HtmlEncode(link);
        // A table-based button: mail apps don't all support modern CSS.
        var html = $"""
                <div style="font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#1f2937;line-height:1.5">
                  <p>Hello {WebUtility.HtmlEncode(user.Name)},</p>
                  <p>Someone asked to reset the password of your {WebUtility.HtmlEncode(appName)} account. Click the button to choose a new one:</p>
                  <table role="presentation" cellspacing="0" cellpadding="0" style="margin:20px 0">
                    <tr>
                      <td style="border-radius:8px;background:#465fff">
                        <a href="{href}" style="display:inline-block;padding:12px 24px;color:#ffffff;font-weight:bold;text-decoration:none;border-radius:8px">Reset password</a>
                      </td>
                    </tr>
                  </table>
                  <p>Or open this link: <a href="{href}">{href}</a></p>
                  <p style="color:#6b7280">The link works for {minutes} minutes and only once. If you didn't ask for it, ignore this email: your password stays as it is.</p>
                </div>
                """;
        var plain = $"Hello {user.Name},\n\nUse this link to choose a new {appName} password:\n{link}\n\n" +
                    $"It works for {minutes} minutes and only once. If you didn't ask for it, ignore this email.";

        var message = new MailMessage
        {
            From = new MailAddress(from, appName),
            Subject = $"Reset your {appName} password",
            SubjectEncoding = Encoding.UTF8
        };
        message.To.Add(new MailAddress(user.Email, user.Name));
        // Both versions as alternate views, plain text first: mail apps show the last one they can display.
        // (With Body + IsBodyHtml plus a plain-text view, .NET labels the HTML part text/plain too, so Gmail
        // showed the raw tags.)
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(plain, Encoding.UTF8, MediaTypeNames.Text.Plain));
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, MediaTypeNames.Text.Html));
        return message;
    }

    /// <summary>Where Development without SMTP writes the emails: Email:PickupDirectory, else App_Data/mail under the API.</summary>
    private string PickupFolder()
    {
        var folder = configuration["Email:PickupDirectory"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(environment.ContentRootPath, "App_Data", "mail");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Development without SMTP: the email is written as an .eml file instead of being sent.</summary>
    private static SmtpClient LocalClient(string folder)
        => new() { DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory, PickupDirectoryLocation = folder };

    private SmtpClient SmtpClientFor(string host)
    {
        var client = new SmtpClient(host, configuration.GetValue("Email:Smtp:Port", 587))
        {
            EnableSsl = configuration.GetValue("Email:Smtp:EnableSsl", true),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        var userName = configuration["Email:Smtp:UserName"];
        if (!string.IsNullOrWhiteSpace(userName))
            client.Credentials = new NetworkCredential(userName, configuration["Email:Smtp:Password"]);
        return client;
    }
}
