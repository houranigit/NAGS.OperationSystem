using Microsoft.AspNetCore.SignalR;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;

namespace Operations.Presentation.Mobile.Sync;

/// <summary>
/// Scoped <see cref="IMobileSyncBroadcaster"/> backed by the SignalR
/// <see cref="MobileSyncHub"/>. Buffers changes inside the request scope so command
/// handlers can fire-and-forget; the <c>MobileSyncBroadcastBehavior</c> pipeline
/// step drains the buffer once the unit-of-work commits.
/// </summary>
/// <remarks>
/// The audience string in each envelope determines the SignalR fan-out:
/// <list type="bullet">
///   <item><c>employee:{guid}</c> → <c>Clients.Group(...)</c> targeting one user.</item>
///   <item><c>station:{IATA}</c> → <c>Clients.Group(...)</c> targeting every connected
///         employee at that station.</item>
///   <item><c>all-stations</c> → <c>Clients.All</c>, used for catalogs the entire fleet
///         shares.</item>
/// </list>
/// De-duplication on flush collapses repeat <c>(table, entityId, op)</c> tuples to a
/// single send so a handler that touches the same flight twice in one request only
/// produces one wire event.
/// </remarks>
public sealed class SignalRMobileSyncBroadcaster(
    IHubContext<MobileSyncHub> hub)
    : IMobileSyncBroadcaster
{
    private readonly List<MobileSyncChange> _buffer = new();
    private bool _flushed;

    public void Enqueue(MobileSyncChange change)
    {
        if (_flushed)
        {
            // Defensive: a handler that enqueues after the behavior has already
            // flushed (e.g. via a fire-and-forget Task.Run inside the handler)
            // would otherwise silently drop the change. Throwing here makes the
            // misuse loud during development; in production this code path is
            // unreachable since handlers are awaited inside the pipeline.
            throw new InvalidOperationException(
                "Cannot enqueue a mobile sync change after the broadcaster has been flushed.");
        }

        _buffer.Add(change);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_flushed || _buffer.Count == 0)
        {
            _flushed = true;
            return;
        }

        _flushed = true;

        // Last-write-wins de-dup on (table, entityId, op) keeps the wire lean when a
        // handler updates the same row twice (e.g. status then assignment) — keeping
        // the LAST envelope is correct because it carries the freshest payload/version.
        var collapsed = _buffer
            .GroupBy(c => (c.Table, c.EntityId, c.Op))
            .Select(g => g.Last())
            .ToList();

        foreach (var change in collapsed)
        {
            await SendAsync(change, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task BroadcastNowAsync(MobileSyncChange change, CancellationToken cancellationToken = default) =>
        SendAsync(change, cancellationToken);

    private Task SendAsync(MobileSyncChange change, CancellationToken cancellationToken)
    {
        var clients = ResolveClients(change.Audience);
        return clients.SendAsync(MobileSyncHub.ChangeClientMethod, change, cancellationToken);
    }

    private IClientProxy ResolveClients(string audience)
    {
        if (string.Equals(audience, MobileSyncAudience.AllStations, StringComparison.Ordinal))
            return hub.Clients.All;

        // Both prefixed audiences map cleanly to one SignalR group — the hub joins
        // every connection to both its employee and station groups on connect, so
        // the same Group(...) call works for either prefix.
        return hub.Clients.Group(audience);
    }
}
