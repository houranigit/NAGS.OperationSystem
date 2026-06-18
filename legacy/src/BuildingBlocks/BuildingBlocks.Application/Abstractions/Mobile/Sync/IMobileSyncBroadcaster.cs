namespace BuildingBlocks.Application.Abstractions.Mobile.Sync;

/// <summary>
/// Per-request buffer + transport for mobile-sync push events. Command handlers
/// in any module call <see cref="Enqueue"/> after they finish mutating the
/// aggregate but before the outer transaction commits; the
/// <c>MobileSyncBroadcastBehavior</c> drains the buffer via <see cref="FlushAsync"/>
/// after a successful save so we never push a change that ended up rolled back.
/// </summary>
/// <remarks>
/// Lives in BuildingBlocks so any module (Operations, Core, Store, …) can inject
/// it without taking a dependency on the Operations module that hosts the
/// SignalR-backed implementation. Registered as a scoped service so each MediatR
/// request gets its own buffer. Failures during flush are logged and swallowed —
/// real-time pushes are an optimisation, never a correctness boundary (the
/// catch-up endpoint and periodic polling cover any missed messages).
/// </remarks>
public interface IMobileSyncBroadcaster
{
    /// <summary>
    /// Append one change envelope to the per-request buffer. Safe to call multiple
    /// times in the same handler — <see cref="FlushAsync"/> de-duplicates by
    /// <c>(Table, EntityId, Op)</c> so a handler that updates the same row twice
    /// produces a single broadcast.
    /// </summary>
    void Enqueue(MobileSyncChange change);

    /// <summary>
    /// Drain the buffer and broadcast each change. Called by the MediatR pipeline
    /// behaviour after the unit-of-work commits; idempotent (a second call on the
    /// same scope is a no-op).
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Immediately broadcast a single change envelope, bypassing the per-request
    /// buffer. Use this from integration event handlers (post-commit by definition)
    /// or other call sites that aren't wrapped by <c>MobileSyncBroadcastBehavior</c> —
    /// e.g. Quartz jobs draining the outbox.
    /// </summary>
    Task BroadcastNowAsync(MobileSyncChange change, CancellationToken cancellationToken = default);
}
