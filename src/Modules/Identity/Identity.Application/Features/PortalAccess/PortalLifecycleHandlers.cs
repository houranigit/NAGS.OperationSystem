using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Contracts.Messaging;
using Identity.Application.Abstractions;
using Identity.Contracts;
using Identity.Domain.Users;
using MasterData.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Identity.Application.Features.PortalAccess;

/// <summary>
/// Propagates a MasterData record deactivation (or permanent removal) onto its linked portal user:
/// the account is deactivated and its active sessions revoked; a permanent removal also releases the
/// login email for reuse. Idempotent via the inbox. Replies with <see cref="PortalUserDeactivated"/>.
/// </summary>
public sealed class LinkedRecordDeactivatedHandler(
    IIdentityDbContext db,
    TimeProvider timeProvider,
    ILogger<LinkedRecordDeactivatedHandler> logger)
    : IIntegrationEventHandler<LinkedRecordDeactivated>
{
    private const string Consumer = "Identity.LinkedRecordDeactivated";

    public async Task HandleAsync(LinkedRecordDeactivated integrationEvent, CancellationToken cancellationToken = default)
    {
        if (await db.HasProcessedAsync(integrationEvent.EventId, Consumer, cancellationToken))
            return;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == integrationEvent.UserId, cancellationToken);
        if (user is null)
        {
            logger.LogWarning("LinkedRecordDeactivated referenced unknown user {UserId}.", integrationEvent.UserId);
            db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var now = timeProvider.GetUtcNow();
        if (integrationEvent.ReleaseEmail)
            user.ReleaseLoginEmail(now);
        else
            user.Deactivate(now);

        var sessions = await db.Sessions
            .Where(s => s.UserId == user.Id && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
            session.Revoke(now);

        db.Enqueue(new PortalUserDeactivated
        {
            ExternalReferenceId = integrationEvent.ExternalReferenceId,
            UserId = user.Id
        });
        db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Starts the Identity-owned reverification when a linked record's email changes. The login email only
/// changes after the new address is verified (via the confirm-email-change endpoint), so an
/// undeliverable address cannot lock the account out. Idempotent via the inbox.
/// </summary>
public sealed class LinkedEmailChangeRequestedHandler(
    IIdentityDbContext db,
    TimeProvider timeProvider,
    ILogger<LinkedEmailChangeRequestedHandler> logger)
    : IIntegrationEventHandler<LinkedEmailChangeRequested>
{
    private const string Consumer = "Identity.LinkedEmailChangeRequested";

    public async Task HandleAsync(LinkedEmailChangeRequested integrationEvent, CancellationToken cancellationToken = default)
    {
        if (await db.HasProcessedAsync(integrationEvent.EventId, Consumer, cancellationToken))
            return;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == integrationEvent.UserId, cancellationToken);
        if (user is null)
        {
            logger.LogWarning("LinkedEmailChangeRequested referenced unknown user {UserId}.", integrationEvent.UserId);
            db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var emailResult = Email.Create(integrationEvent.NewEmail);
        if (emailResult.IsFailure)
        {
            logger.LogError("LinkedEmailChangeRequested for user {UserId} had an invalid email '{Email}'.",
                integrationEvent.UserId, integrationEvent.NewEmail);
            db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var now = timeProvider.GetUtcNow();
        var token = Guid.NewGuid();
        var change = user.RequestEmailChange(emailResult.Value, token, now.AddHours(72), now);
        if (change.IsFailure)
        {
            logger.LogWarning("Email change for user {UserId} was rejected: {Reason}.", integrationEvent.UserId, change.Error.Description);
            db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        // Verification delivery reuses the email infrastructure once wired for change flows; for now the
        // pending state is recorded and the token logged for development confirmation.
        logger.LogInformation("Email-change verification token issued for user {UserId}.", integrationEvent.UserId);

        db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
        await db.SaveChangesAsync(cancellationToken);
    }
}
