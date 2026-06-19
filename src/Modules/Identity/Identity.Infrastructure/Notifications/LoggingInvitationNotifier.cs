using Identity.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Identity.Infrastructure.Notifications;

/// <summary>
/// v1.0.0 invitation delivery: logs the invitation instead of sending email. Replace with an
/// SMTP/email-provider implementation without touching the invite handlers.
/// </summary>
public sealed class LoggingInvitationNotifier(ILogger<LoggingInvitationNotifier> logger) : IInvitationNotifier
{
    public Task SendInvitationAsync(string email, string displayName, Guid userId, Guid invitationToken, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Invitation for {Email} (user {UserId}). Activation token: {InvitationToken}",
            email, userId, invitationToken);
        return Task.CompletedTask;
    }
}
