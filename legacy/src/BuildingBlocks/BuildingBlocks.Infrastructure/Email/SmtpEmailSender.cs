using System.Net;
using System.Net.Mail;
using BuildingBlocks.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Infrastructure.Email;

/// <summary>
/// SMTP-based <see cref="IEmailSender"/> built on <see cref="System.Net.Mail.SmtpClient"/>. Honours
/// <see cref="EmailSettings.EnableEmailNotifications"/> — when disabled the message is logged and the
/// call returns successfully so feature handlers don't need to branch on environment.
/// </summary>
public sealed class SmtpEmailSender(
    IOptionsMonitor<EmailSettings> settingsMonitor,
    ILogger<SmtpEmailSender> logger)
    : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var settings = settingsMonitor.CurrentValue;

        if (!settings.EnableEmailNotifications)
        {
            logger.LogInformation(
                "Email notifications are disabled. Skipping send to {To} with subject {Subject}.",
                message.ToAddress, message.Subject);
            return;
        }

        var fromAddress = string.IsNullOrWhiteSpace(message.FromAddress)
            ? settings.DefaultFromEmail
            : message.FromAddress;

        if (string.IsNullOrWhiteSpace(fromAddress))
            throw new InvalidOperationException("Cannot send email: no DefaultFromEmail configured.");

        var fromDisplayName = string.IsNullOrWhiteSpace(message.FromDisplayName)
            ? (settings.DefaultFromDisplayName ?? fromAddress)
            : message.FromDisplayName;

        using var mail = new MailMessage
        {
            From = new MailAddress(fromAddress, fromDisplayName),
            Subject = message.Subject,
            IsBodyHtml = true,
            Body = message.HtmlBody
        };

        mail.To.Add(string.IsNullOrWhiteSpace(message.ToDisplayName)
            ? new MailAddress(message.ToAddress)
            : new MailAddress(message.ToAddress, message.ToDisplayName));

        if (!string.IsNullOrWhiteSpace(message.PlainTextBody))
        {
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.PlainTextBody, null, "text/plain"));
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.HtmlBody, null, "text/html"));
        }

        using var smtp = new SmtpClient(settings.SmtpSettings.Server, settings.SmtpSettings.Port)
        {
            EnableSsl = settings.SmtpSettings.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(
                settings.SmtpSettings.Username,
                settings.SmtpSettings.Password)
        };

        try
        {
            await smtp.SendMailAsync(mail, cancellationToken);
            logger.LogInformation("Email sent to {To} (subject: {Subject})", message.ToAddress, message.Subject);
        }
        catch (Exception ex)
        {
            // Swallow + log so a misconfigured SMTP host can't break the workflow that triggered the email
            // (invitation, password reset, etc.). Callers see a successful path and the operator sees the log.
            logger.LogError(ex, "Failed to send email to {To} (subject: {Subject})", message.ToAddress, message.Subject);
        }
    }
}
