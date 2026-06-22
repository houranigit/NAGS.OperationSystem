using System.Net;
using System.Net.Mail;
using BuildingBlocks.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Infrastructure.Email;

/// <summary>
/// SMTP delivery. When notifications are disabled it logs instead of connecting, so development and
/// tests never attempt SMTP. A delivery failure throws so the caller can keep the account in an
/// <c>Invited</c> state and surface a retryable error; it never silently swallows failures.
/// </summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public bool IsEnabled => _options.EnableEmailNotifications;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!_options.EnableEmailNotifications)
        {
            logger.LogInformation("Email delivery disabled; not sending '{Subject}' to {Email}.", message.Subject, message.ToEmail);
            return;
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(_options.FromEmail, _options.FromName),
            Subject = message.Subject,
            Body = message.HtmlBody,
            IsBodyHtml = true
        };
        mail.To.Add(new MailAddress(message.ToEmail, message.ToName));

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        if (!string.IsNullOrWhiteSpace(_options.Username))
            client.Credentials = new NetworkCredential(_options.Username, _options.Password);

        await client.SendMailAsync(mail, cancellationToken);
        logger.LogInformation("Sent email '{Subject}' to {Email}.", message.Subject, message.ToEmail);
    }
}
