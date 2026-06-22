namespace BuildingBlocks.Application.Abstractions;

/// <summary>A single outbound email message (HTML body).</summary>
public sealed record EmailMessage(string ToEmail, string ToName, string Subject, string HtmlBody);

/// <summary>
/// Transport-agnostic email delivery. v1.0.0 ships an SMTP sender and a no-op sender selected by
/// configuration; callers (e.g. Identity invitations) never talk to SMTP directly.
/// </summary>
public interface IEmailSender
{
    /// <summary>True when a real transport is configured and enabled.</summary>
    public bool IsEnabled { get; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
