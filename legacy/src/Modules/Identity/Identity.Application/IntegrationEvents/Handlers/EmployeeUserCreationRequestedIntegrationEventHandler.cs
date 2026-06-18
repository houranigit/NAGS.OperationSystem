using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.IntegrationEvents;
using Core.Contracts.IntegrationEvents;
using Identity.Application.Abstractions;
using Identity.Application.EmailTemplates;
using Identity.Contracts.IntegrationEvents;
using Identity.Domain.Aggregates.User;
using Identity.Domain.Enumerations;
using Identity.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Identity.Application.IntegrationEvents.Handlers;

/// <summary>
/// Cross-module consumer of <see cref="EmployeeUserCreationRequestedIntegrationEvent"/>: provisions
/// an invited <see cref="User"/> for the new employee and emails an activation link. Idempotent via
/// the standard inbox-dedup row keyed by <c>EventId</c>; existing users with the same email short-circuit
/// safely (we don't fail the workflow — the operator may have created the user manually first).
/// </summary>
/// <remarks>
/// Username derivation: takes the local part of the email and falls back to the integration event
/// id when uniqueness collides. The invitation token is generated through the standard
/// <see cref="IInvitationTokenGenerator"/> with a 7-day expiry, mirroring <c>InviteUser</c>.
/// </remarks>
public sealed class EmployeeUserCreationRequestedIntegrationEventHandler(
    IIdentityDbContext db,
    IUserRepository userRepository,
    IInvitationTokenGenerator tokenGenerator,
    IEmailSender emailSender,
    IInvitationEmailComposer emailComposer,
    IOutboxWriter outboxWriter,
    ILogger<EmployeeUserCreationRequestedIntegrationEventHandler> logger)
    : IIntegrationEventHandler<EmployeeUserCreationRequestedIntegrationEvent>
{
    public async Task Handle(
        EmployeeUserCreationRequestedIntegrationEvent notification,
        CancellationToken cancellationToken)
    {
        if (await db.IsAlreadyProcessedAsync(notification.EventId, cancellationToken))
            return;

        // Skip silently if the email is already taken — re-using/linking a manually-created user
        // is an admin decision and not the responsibility of this auto-provisioning path.
        var existing = await userRepository.GetByEmailAsync(notification.Email, cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation(
                "Skipping user creation for employee {EmployeeId}: a user with email {Email} already exists.",
                notification.EmployeeId, notification.Email);
            db.MarkProcessed(notification.EventId, nameof(EmployeeUserCreationRequestedIntegrationEvent));
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var emailResult = Email.Create(notification.Email);
        if (emailResult.IsFailure)
        {
            logger.LogWarning(
                "Cannot invite user for employee {EmployeeId}: invalid email '{Email}' ({Error}).",
                notification.EmployeeId, notification.Email, emailResult.Error.Description);
            db.MarkProcessed(notification.EventId, nameof(EmployeeUserCreationRequestedIntegrationEvent));
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var username = await BuildUniqueUsernameAsync(notification.Email, cancellationToken);
        var usernameResult = Username.Create(username);
        if (usernameResult.IsFailure)
        {
            logger.LogWarning(
                "Cannot invite user for employee {EmployeeId}: derived username '{Username}' is invalid ({Error}).",
                notification.EmployeeId, username, usernameResult.Error.Description);
            db.MarkProcessed(notification.EventId, nameof(EmployeeUserCreationRequestedIntegrationEvent));
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var token = tokenGenerator.Generate();
        var expiresAt = DateTime.UtcNow.Add(tokenGenerator.ExpiryDuration);

        var inviteResult = User.Invite(
            usernameResult.Value,
            emailResult.Value,
            UserType.Employee,
            token,
            expiresAt,
            externalReferenceId: notification.EmployeeId);

        if (inviteResult.IsFailure)
        {
            logger.LogError(
                "Failed to invite user for employee {EmployeeId}: {Error}",
                notification.EmployeeId, inviteResult.Error.Description);
            db.MarkProcessed(notification.EventId, nameof(EmployeeUserCreationRequestedIntegrationEvent));
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        userRepository.Add(inviteResult.Value);

        // Reply to Core so it can set Employee.LinkedUserId. Written to the outbox in the same
        // transaction as the User insert + inbox-dedup row — guarantees we never lose the link
        // even if the Identity process dies right after creating the user.
        outboxWriter.Write(
            nameof(UserCreatedForEmployeeIntegrationEvent),
            JsonSerializer.Serialize(new UserCreatedForEmployeeIntegrationEvent(
                EmployeeId: notification.EmployeeId,
                UserId: inviteResult.Value.Id.Value)));

        db.MarkProcessed(notification.EventId, nameof(EmployeeUserCreationRequestedIntegrationEvent));
        await db.SaveChangesAsync(cancellationToken);

        var invitationEmail = emailComposer.BuildInvitation(
            recipientEmail: emailResult.Value.Value,
            recipientDisplayName: notification.FullName,
            invitationToken: token,
            expiresAtUtc: expiresAt);

        await emailSender.SendAsync(invitationEmail, cancellationToken);
    }

    private async Task<string> BuildUniqueUsernameAsync(string email, CancellationToken ct)
    {
        var atIdx = email.IndexOf('@');
        var local = atIdx > 0 ? email[..atIdx] : email;

        // Username has its own validation rules — strip everything that isn't safe up front.
        var safeChars = local
            .Where(c => char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-')
            .ToArray();

        var baseName = new string(safeChars).Trim('.', '_', '-');
        if (baseName.Length < 3)
            baseName = "user" + Guid.NewGuid().ToString("N")[..6];
        if (baseName.Length > 45)
            baseName = baseName[..45];

        var candidate = baseName;
        var attempt = 0;
        while (await userRepository.GetByEmailOrUsernameAsync(candidate, ct) is not null)
        {
            attempt++;
            var suffix = attempt.ToString();
            var trimmed = baseName.Length + suffix.Length > 50
                ? baseName[..(50 - suffix.Length)]
                : baseName;
            candidate = trimmed + suffix;
            if (attempt > 50)
            {
                candidate = "user" + Guid.NewGuid().ToString("N")[..8];
                break;
            }
        }

        return candidate;
    }
}
