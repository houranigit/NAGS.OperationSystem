using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Messaging;
using Operations.Contracts;

namespace Operations.Application.Features.WorkOrders;

/// <summary>
/// Deletes a detached work-order blob from durable storage. Storage deletion is expected to be
/// idempotent; if it fails, the originating Operations outbox retains and retries this event.
/// </summary>
public sealed class WorkOrderFileDeletionRequestedHandler(IFileStorage storage)
    : IIntegrationEventHandler<WorkOrderFileDeletionRequested>
{
    public Task HandleAsync(
        WorkOrderFileDeletionRequested integrationEvent,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(integrationEvent.StorageReference))
            throw new InvalidOperationException("A work-order file deletion requires a storage reference.");

        return storage.DeleteAsync(integrationEvent.StorageReference, cancellationToken);
    }
}
