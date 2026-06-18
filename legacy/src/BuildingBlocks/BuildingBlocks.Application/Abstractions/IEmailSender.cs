namespace BuildingBlocks.Application.Abstractions;

/// <summary>
/// Sends transactional emails (invitations, password resets, notifications).
/// Implementations honour <c>EmailSettings.EnableEmailNotifications</c> and silently no-op when disabled.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed record EmailMessage(
    string ToAddress,
    string Subject,
    string HtmlBody,
    string? ToDisplayName = null,
    string? PlainTextBody = null,
    string? FromAddress = null,
    string? FromDisplayName = null);
