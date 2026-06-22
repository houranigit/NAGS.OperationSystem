using BuildingBlocks.Application.Messaging;
using MasterData.Contracts;

namespace MasterData.Application.Features.PortalAccess;

/// <summary>
/// Helpers that enqueue cross-module lifecycle events when a MasterData record with a linked portal
/// user is deactivated, removed, or has its linked email changed. Each event is written to the module
/// outbox in the same transaction as the state change and dispatched to Identity by the outbox processor.
/// </summary>
internal static class PortalLifecycle
{
    public static void EnqueueDeactivation(IOutboxDbContext db, Guid externalReferenceId, Guid linkedUserId, bool releaseEmail = false) =>
        db.Enqueue(new LinkedRecordDeactivated
        {
            ExternalReferenceId = externalReferenceId,
            UserId = linkedUserId,
            ReleaseEmail = releaseEmail
        });

    public static void EnqueueEmailChange(IOutboxDbContext db, Guid externalReferenceId, Guid linkedUserId, string newEmail) =>
        db.Enqueue(new LinkedEmailChangeRequested
        {
            ExternalReferenceId = externalReferenceId,
            UserId = linkedUserId,
            NewEmail = newEmail
        });
}
