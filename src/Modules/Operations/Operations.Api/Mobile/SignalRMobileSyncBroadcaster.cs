using BuildingBlocks.Application.Mobile;
using Microsoft.AspNetCore.SignalR;

namespace Operations.Api.Mobile;

/// <summary>
/// Scoped <see cref="IMobileSyncBroadcaster"/> backed by the SignalR <see cref="MobileSyncHub"/>.
/// Buffers changes inside the request scope so command handlers can fire-and-forget; the
/// <c>MobileSyncBroadcastBehavior</c> pipeline step drains the buffer after the handler succeeds.
/// </summary>
/// <remarks>
/// The audience string picks the fan-out: <c>employee:{guid}</c> and <c>station:{IATA}</c>
/// map to SignalR groups the hub joins on connect; <c>all-stations</c> maps to
/// <c>Clients.All</c>. Flush collapses repeat <c>(table, entityId, op, audience)</c> tuples,
/// keeping the last (freshest) envelope.
/// </remarks>
public sealed class SignalRMobileSyncBroadcaster(IHubContext<MobileSyncHub> hub) : IMobileSyncBroadcaster
{
    private readonly List<MobileSyncChange> _buffer = [];

    public void Enqueue(MobileSyncChange change) => _buffer.Add(change);

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_buffer.Count == 0)
            return;

        var collapsed = _buffer
            .GroupBy(c => (c.Table, c.EntityId, c.Op, c.Audience))
            .Select(g => g.Last())
            .ToList();

        // Reset before sending so a scope that runs several commands (each flushed by its own
        // pipeline pass) buffers and broadcasts each command's changes independently.
        _buffer.Clear();

        foreach (var change in collapsed)
            await SendAsync(change, cancellationToken).ConfigureAwait(false);
    }

    public Task BroadcastNowAsync(MobileSyncChange change, CancellationToken cancellationToken = default) =>
        SendAsync(change, cancellationToken);

    private Task SendAsync(MobileSyncChange change, CancellationToken cancellationToken)
    {
        var clients = string.Equals(change.Audience, MobileSyncAudience.AllStations, StringComparison.Ordinal)
            ? hub.Clients.All
            : hub.Clients.Group(change.Audience);

        return clients.SendAsync(MobileSyncHub.ChangeClientMethod, change, cancellationToken);
    }
}
