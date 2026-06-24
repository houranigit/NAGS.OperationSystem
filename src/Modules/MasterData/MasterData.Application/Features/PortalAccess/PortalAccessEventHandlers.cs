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
/// Reflects an Identity-side deactivation back onto the MasterData record by clearing the linked
/// user. Defensive and idempotent; the link is also cleared when MasterData itself removes a record.
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
            .FirstOrDefaultAsync(s => s.Id == integrationEvent.ExternalReferenceId, cancellationToken);
        staff?.UnlinkUser(now);

        var contact = await db.CustomerContacts
            .FirstOrDefaultAsync(c => c.Id == integrationEvent.ExternalReferenceId, cancellationToken);
        contact?.UnlinkUser(now);

        db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
        await db.SaveChangesAsync(cancellationToken);
    }
}
