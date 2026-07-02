using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Contracts.Messaging;
using Identity.Application.Abstractions;
using Identity.Contracts;
using Identity.Domain.Users;
using MasterData.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Identity.Application.Features.PortalAccess;

/// <summary>
/// Propagates a MasterData record deactivation (or permanent removal) onto its linked portal user:
/// the account is suspended and its active sessions revoked; a permanent removal also detaches the
/// user and releases the login email for reuse. Idempotent via the inbox. Replies with
/// <see cref="PortalUserDeactivated"/>.
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
        {
            user.ReleaseLoginEmail(now);
        }
        else
        {
            var suspend = user.Suspend(now);
            if (suspend.IsFailure)
            {
                logger.LogWarning(
                    "LinkedRecordDeactivated could not suspend user {UserId}: {Reason}.",
                    user.Id,
                    suspend.Error.Description);
            }
        }

        var sessions = await db.Sessions
            .Where(s => s.UserId == user.Id && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
            session.Revoke(now);

        db.Enqueue(new PortalUserDeactivated
        {
            ExternalReferenceId = integrationEvent.ExternalReferenceId,
            UserId = user.Id,
            ReleaseEmail = integrationEvent.ReleaseEmail
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
    ITokenService tokenService,
    ILinkedEmailVerificationNotifier verificationNotifier,
    TimeProvider timeProvider,
    IOptions<IdentityModuleOptions> options,
    ILogger<LinkedEmailChangeRequestedHandler> logger)
    : IIntegrationEventHandler<LinkedEmailChangeRequested>
{
    private const string Consumer = "Identity.LinkedEmailChangeRequested";
    private readonly IdentityModuleOptions _options = options.Value;

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

        // Duplicate handling: if the new address is already a live login email, do not start a change.
        var newEmailValue = emailResult.Value.Value;
        var taken = await db.Users.AnyAsync(
            u => u.Email.Value == newEmailValue && !u.LoginEmailReleased && u.Id != user.Id, cancellationToken);
        if (taken)
        {
            logger.LogWarning("Linked email change for user {UserId} skipped: '{Email}' already in use.", integrationEvent.UserId, newEmailValue);
            db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var token = tokenService.CreateSecureToken();
        var change = user.RequestEmailChange(
            emailResult.Value,
            token.Hash,
            now.AddHours(_options.EmailChangeExpiryHours),
            now);
        if (change.IsFailure)
        {
            logger.LogWarning("Email change for user {UserId} was rejected: {Reason}.", integrationEvent.UserId, change.Error.Description);
            db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
        await db.SaveChangesAsync(cancellationToken);

        // Durable verification delivery to the pending address. The login email stays unchanged until
        // the recipient confirms via the emailed link.
        await verificationNotifier.SendVerificationAsync(newEmailValue, user.DisplayName, user.Id, token.Value, cancellationToken);
    }
}
