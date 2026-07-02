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
/// Provisions an invited portal <c>User</c> from a MasterData portal-access request. The account
/// always originates from a MasterData identity (no manual user creation). Idempotent via the inbox
/// and the unique <see cref="User.ExternalReferenceId"/>; never creates two users for one record.
/// On success it enqueues <see cref="PortalUserProvisioned"/> so MasterData can store the link, then
/// sends the invitation after the user is committed.
/// </summary>
public sealed class PortalAccessRequestedHandler(
    IIdentityDbContext db,
    IInvitationNotifier invitationNotifier,
    ITokenService tokenService,
    TimeProvider timeProvider,
    IOptions<IdentityModuleOptions> options,
    ILogger<PortalAccessRequestedHandler> logger)
    : IIntegrationEventHandler<PortalAccessRequested>
{
    private const string Consumer = "Identity.PortalAccessRequested";
    private readonly IdentityModuleOptions _options = options.Value;

    public async Task HandleAsync(PortalAccessRequested integrationEvent, CancellationToken cancellationToken = default)
    {
        if (await db.HasProcessedAsync(integrationEvent.EventId, Consumer, cancellationToken))
            return;

        // Idempotency: a live account already exists for this MasterData record. Re-announce the link
        // in case the original reply was lost, then stop. Never create a second user for one record.
        var existing = await db.Users.FirstOrDefaultAsync(
            u => u.UserType == integrationEvent.UserType &&
                u.ExternalReferenceId == integrationEvent.ExternalReferenceId &&
                !u.LoginEmailReleased,
            cancellationToken);
        if (existing is not null)
        {
            db.Enqueue(new PortalUserProvisioned
            {
                ExternalReferenceId = integrationEvent.ExternalReferenceId,
                UserId = existing.Id,
                UserType = existing.UserType,
                Email = existing.Email.Value,
                CorrelationId = integrationEvent.CorrelationId
            });
            db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var emailResult = Email.Create(integrationEvent.Email);
        if (emailResult.IsFailure)
        {
            await GiveUpAsync(integrationEvent, $"invalid email '{integrationEvent.Email}'", cancellationToken);
            return;
        }

        var email = emailResult.Value;
        var emailValue = email.Value;

        var emailTaken = await db.Users.AnyAsync(u => u.Email.Value == emailValue && !u.LoginEmailReleased, cancellationToken);
        if (emailTaken)
        {
            await GiveUpAsync(integrationEvent, $"the login email '{emailValue}' is already in use", cancellationToken);
            return;
        }

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == integrationEvent.RoleId, cancellationToken);
        if (role is null)
        {
            await GiveUpAsync(integrationEvent, $"role '{integrationEvent.RoleId}' does not exist", cancellationToken);
            return;
        }

        if (role.CompatibleUserType != integrationEvent.UserType)
        {
            await GiveUpAsync(integrationEvent,
                $"role '{integrationEvent.RoleId}' is not compatible with user type '{integrationEvent.UserType}'", cancellationToken);
            return;
        }

        var now = timeProvider.GetUtcNow();
        var token = tokenService.CreateSecureToken();
        var expiry = now.AddHours(_options.InvitationExpiryHours);

        var userResult = User.Invite(
            email, integrationEvent.DisplayName, integrationEvent.RoleId, token.Hash, expiry, now,
            integrationEvent.UserType, integrationEvent.ExternalReferenceId);
        if (userResult.IsFailure)
        {
            await GiveUpAsync(integrationEvent, userResult.Error.Description, cancellationToken);
            return;
        }

        var user = userResult.Value;
        db.Users.Add(user);
        db.Enqueue(new PortalUserProvisioned
        {
            ExternalReferenceId = integrationEvent.ExternalReferenceId,
            UserId = user.Id,
            UserType = user.UserType,
            Email = emailValue,
            CorrelationId = integrationEvent.CorrelationId
        });
        db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
        await db.SaveChangesAsync(cancellationToken);

        // Delivery happens only after the invited user is committed. A failure is logged and does not
        // roll back the account; the administrator can use Resend invitation.
        try
        {
            await invitationNotifier.SendInvitationAsync(emailValue, user.DisplayName, user.Id, token.Value, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Invitation delivery failed for provisioned user {UserId}.", user.Id);
        }
    }

    /// <summary>
    /// Records a non-retryable provisioning failure (bad email, missing/incompatible role, duplicate
    /// email) and consumes the message so it does not loop. The administrator can re-grant access after
    /// correcting the cause, which raises a fresh request.
    /// </summary>
    private async Task GiveUpAsync(PortalAccessRequested integrationEvent, string reason, CancellationToken cancellationToken)
    {
        logger.LogError(
            "Portal access provisioning failed for record {ExternalReferenceId} ({UserType}): {Reason}.",
            integrationEvent.ExternalReferenceId, integrationEvent.UserType, reason);

        // Reply with a visible, retryable failure so MasterData can surface it.
        db.Enqueue(new PortalUserProvisioningFailed
        {
            ExternalReferenceId = integrationEvent.ExternalReferenceId,
            UserType = integrationEvent.UserType,
            Reason = reason,
            CorrelationId = integrationEvent.CorrelationId
        });

        db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
        await db.SaveChangesAsync(cancellationToken);
    }
}
