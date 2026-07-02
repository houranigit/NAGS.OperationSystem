using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Contracts.Messaging;
using Identity.Contracts;
using MasterData.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.PortalAccess;

/// <summary>
/// Stores the linked portal <c>User</c> id on the originating MasterData record once Identity has
/// provisioned the invited account. Idempotent via the module inbox.
/// </summary>
public sealed class PortalUserProvisionedHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : IIntegrationEventHandler<PortalUserProvisioned>
{
    private const string Consumer = "MasterData.PortalUserProvisioned";

    public async Task HandleAsync(PortalUserProvisioned integrationEvent, CancellationToken cancellationToken = default)
    {
        if (await db.HasProcessedAsync(integrationEvent.EventId, Consumer, cancellationToken))
            return;

        var now = timeProvider.GetUtcNow();

        switch (integrationEvent.UserType)
        {
            case UserType.StationStaff:
                var staff = await db.StaffMembers
                    .FirstOrDefaultAsync(s => s.Id == integrationEvent.ExternalReferenceId, cancellationToken);
                staff?.LinkUser(integrationEvent.UserId, integrationEvent.CorrelationId, now);
                break;

            case UserType.CustomerContact:
                var contact = await db.CustomerContacts
                    .FirstOrDefaultAsync(c => c.Id == integrationEvent.ExternalReferenceId, cancellationToken);
                contact?.LinkUser(integrationEvent.UserId, integrationEvent.CorrelationId, now);
                break;
        }

        db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Records a provisioning failure on the originating record so it is visible and the grant can be
/// retried. Correlation-guarded so a stale failure cannot clobber a newer successful provision.
/// </summary>
public sealed class PortalUserProvisioningFailedHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : IIntegrationEventHandler<PortalUserProvisioningFailed>
{
    private const string Consumer = "MasterData.PortalUserProvisioningFailed";

    public async Task HandleAsync(PortalUserProvisioningFailed integrationEvent, CancellationToken cancellationToken = default)
    {
        if (await db.HasProcessedAsync(integrationEvent.EventId, Consumer, cancellationToken))
            return;

        var now = timeProvider.GetUtcNow();

        switch (integrationEvent.UserType)
        {
            case UserType.StationStaff:
                var staff = await db.StaffMembers.FirstOrDefaultAsync(s => s.Id == integrationEvent.ExternalReferenceId, cancellationToken);
                staff?.MarkPortalFailed(integrationEvent.CorrelationId, integrationEvent.Reason, now);
                break;

            case UserType.CustomerContact:
                var contact = await db.CustomerContacts.FirstOrDefaultAsync(c => c.Id == integrationEvent.ExternalReferenceId, cancellationToken);
                contact?.MarkPortalFailed(integrationEvent.CorrelationId, integrationEvent.Reason, now);
                break;
        }

        db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Reflects account activation back onto the MasterData record, guarded so a delayed activation event
/// cannot undo a local suspension or update the wrong linked user.
/// </summary>
public sealed class PortalUserActivatedHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : IIntegrationEventHandler<PortalUserActivated>
{
    private const string Consumer = "MasterData.PortalUserActivated";

    public async Task HandleAsync(PortalUserActivated integrationEvent, CancellationToken cancellationToken = default)
    {
        if (await db.HasProcessedAsync(integrationEvent.EventId, Consumer, cancellationToken))
            return;

        var now = timeProvider.GetUtcNow();

        switch (integrationEvent.UserType)
        {
            case UserType.StationStaff:
                var staff = await db.StaffMembers
                    .FirstOrDefaultAsync(s => s.Id == integrationEvent.ExternalReferenceId, cancellationToken);
                if (staff is not null &&
                    await db.Stations.AnyAsync(s => s.Id == staff.StationId && s.IsActive, cancellationToken))
                {
                    staff.MarkPortalActive(integrationEvent.UserId, now);
                }
                break;

            case UserType.CustomerContact:
                var contact = await db.CustomerContacts
                    .FirstOrDefaultAsync(c => c.Id == integrationEvent.ExternalReferenceId, cancellationToken);
                if (contact is not null &&
                    await db.Customers.AnyAsync(c => c.Id == contact.CustomerId && c.IsActive, cancellationToken))
                {
                    contact.MarkPortalActive(integrationEvent.UserId, now);
                }
                break;
        }

        db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Reflects Identity-side restore access back onto linked MasterData records.</summary>
public sealed class PortalUserAccessRestoredHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : IIntegrationEventHandler<PortalUserAccessRestored>
{
    private const string Consumer = "MasterData.PortalUserAccessRestored";

    public async Task HandleAsync(PortalUserAccessRestored integrationEvent, CancellationToken cancellationToken = default)
    {
        if (await db.HasProcessedAsync(integrationEvent.EventId, Consumer, cancellationToken))
            return;

        var now = timeProvider.GetUtcNow();

        switch (integrationEvent.UserType)
        {
            case UserType.StationStaff:
                var staff = await db.StaffMembers
                    .FirstOrDefaultAsync(s => s.Id == integrationEvent.ExternalReferenceId, cancellationToken);
                if (staff is not null &&
                    await db.Stations.AnyAsync(s => s.Id == staff.StationId && s.IsActive, cancellationToken))
                {
                    if (integrationEvent.IsActivated)
                        staff.MarkPortalActive(integrationEvent.UserId, now);
                    else
                        staff.MarkPortalInvited(integrationEvent.UserId, now);
                }
                break;

            case UserType.CustomerContact:
                var contact = await db.CustomerContacts
                    .FirstOrDefaultAsync(c => c.Id == integrationEvent.ExternalReferenceId, cancellationToken);
                if (contact is not null &&
                    await db.Customers.AnyAsync(c => c.Id == contact.CustomerId && c.IsActive, cancellationToken))
                {
                    if (integrationEvent.IsActivated)
                        contact.MarkPortalActive(integrationEvent.UserId, now);
                    else
                        contact.MarkPortalInvited(integrationEvent.UserId, now);
                }
                break;
        }

        db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Reflects an Identity-side suspension/detachment back onto the MasterData record. Reversible
/// deactivation keeps the link and marks portal access suspended; permanent removal clears the link.
/// </summary>
public sealed class PortalUserDeactivatedHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : IIntegrationEventHandler<PortalUserDeactivated>
{
    private const string Consumer = "MasterData.PortalUserDeactivated";

    public async Task HandleAsync(PortalUserDeactivated integrationEvent, CancellationToken cancellationToken = default)
    {
        if (await db.HasProcessedAsync(integrationEvent.EventId, Consumer, cancellationToken))
            return;

        var now = timeProvider.GetUtcNow();

        var staff = await db.StaffMembers
            .FirstOrDefaultAsync(s =>
                s.Id == integrationEvent.ExternalReferenceId &&
                s.LinkedUserId == integrationEvent.UserId, cancellationToken);
        if (staff is not null)
        {
            if (integrationEvent.ReleaseEmail)
                staff.UnlinkUser(now);
            else
                staff.SuspendPortal(now);
        }

        var contact = await db.CustomerContacts
            .FirstOrDefaultAsync(c =>
                c.Id == integrationEvent.ExternalReferenceId &&
                c.LinkedUserId == integrationEvent.UserId, cancellationToken);
        if (contact is not null)
        {
            if (integrationEvent.ReleaseEmail)
                contact.UnlinkUser(now);
            else
                contact.SuspendPortal(now);
        }

        db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
        await db.SaveChangesAsync(cancellationToken);
    }
}
