namespace App.Application.Security;

/// <summary>
/// Sends the transactional emails the self-service flows need (password reset, email verification,
/// invitations). A no-op/log implementation is used when SMTP isn't configured, so admin-driven flows
/// still work; self-service in production requires a real sender.
/// </summary>
public interface IAppEmailSender
{
    /// <summary>Whether a real sender is configured (self-service flows require this in production).</summary>
    bool IsConfigured { get; }

    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default);
}
