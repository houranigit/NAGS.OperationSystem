using BuildingBlocks.Application.Abstractions;
using Operations.Application.Features.WorkOrders;
using Operations.Contracts;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class WorkOrderFileDeletionRequestedHandlerTests
{
    [Fact]
    public async Task HandleAsync_Deletes_the_requested_storage_reference()
    {
        var storage = new RecordingStorage();
        var handler = new WorkOrderFileDeletionRequestedHandler(storage);

        await handler.HandleAsync(new WorkOrderFileDeletionRequested
        {
            StorageReference = "work-order-attachments/one.pdf"
        });

        storage.Deleted.ShouldBe(["work-order-attachments/one.pdf"]);
    }

    [Fact]
    public async Task HandleAsync_Propagates_storage_failures_for_outbox_retry()
    {
        var handler = new WorkOrderFileDeletionRequestedHandler(
            new RecordingStorage(new IOException("Temporary object-store failure.")));

        var exception = await Should.ThrowAsync<IOException>(() => handler.HandleAsync(
            new WorkOrderFileDeletionRequested
            {
                StorageReference = "work-order-attachments/retry.webm"
            }));

        exception.Message.ShouldBe("Temporary object-store failure.");
    }

    private sealed class RecordingStorage(Exception? deleteFailure = null) : IFileStorage
    {
        public List<string> Deleted { get; } = [];

        public Task<StoredFile> SaveAsync(
            string container,
            string fileName,
            string contentType,
            Stream content,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream?> OpenAsync(
            string storageKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            string storageKey,
            CancellationToken cancellationToken = default)
        {
            Deleted.Add(storageKey);
            return deleteFailure is null
                ? Task.CompletedTask
                : Task.FromException(deleteFailure);
        }
    }
}
