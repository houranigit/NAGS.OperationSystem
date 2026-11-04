using BuildingBlocks.Application.Mobile;

namespace Operations.Application.UnitTests;

internal sealed class NoopMobileSyncBroadcaster : IMobileSyncBroadcaster
{
    public static readonly NoopMobileSyncBroadcaster Instance = new();
    public void Enqueue(MobileSyncChange change) { }
    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task BroadcastNowAsync(MobileSyncChange change, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
