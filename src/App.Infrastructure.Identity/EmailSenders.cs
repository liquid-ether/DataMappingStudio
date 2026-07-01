using System.Net;
using System.Net.Mail;
using App.Application.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Identity;

/// <summary>Sends transactional email over SMTP (used when <c>Auth:Email:Host</c> is configured).</summary>
public sealed class SmtpEmailSender(IOptions<SecurityOptions> options, ILogger<SmtpEmailSender> logger) : IAppEmailSender
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.Email.Host);

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        EmailOptions email = options.Value.Email;
        using SmtpClient client = new(email.Host!, email.Port)
        {
            EnableSsl = email.UseSsl,
            Credentials = string.IsNullOrWhiteSpace(email.UserName) ? null : new NetworkCredential(email.UserName, email.Password),
        };

        using MailMessage message = new(email.From, toEmail, subject, htmlBody) { IsBodyHtml = true };
        try
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (SmtpException ex)
        {
            logger.LogError(ex, "Failed to send email '{Subject}' to {Recipient}.", subject, toEmail);
            throw;
        }
    }
}

/// <summary>
/// Fallback used when no SMTP host is configured: logs that an email would have been sent. The body
/// (which may contain a one-time link) is logged at Debug so it never lands in normal production logs —
/// raise the log level to retrieve links in local development.
/// </summary>
public sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IAppEmailSender
{
    public bool IsConfigured => false;

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        logger.LogWarning("Email not configured (Auth:Email:Host) — would send '{Subject}' to {Recipient}.", subject, toEmail);
        logger.LogDebug("Email body for {Recipient}: {Body}", toEmail, htmlBody);
        return Task.CompletedTask;
    }
}
