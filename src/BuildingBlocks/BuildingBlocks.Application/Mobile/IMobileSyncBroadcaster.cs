namespace BuildingBlocks.Application.Mobile;

/// <summary>
/// Per-request buffer + transport for mobile-sync push events. Command handlers call
/// <see cref="Enqueue"/> after mutating the aggregate; <c>MobileSyncBroadcastBehavior</c>
/// drains the buffer via <see cref="FlushAsync"/> only after the handler completed
/// successfully (i.e. after its SaveChanges committed), so a rolled-back change is
/// never pushed.
/// </summary>
/// <remarks>
/// Lives in BuildingBlocks so any module can enqueue without depending on the module
/// that hosts the SignalR-backed implementation. Registered scoped so each request gets
/// its own buffer. Flush failures are logged and swallowed — real-time pushes are an
/// optimisation, never a correctness boundary (the catch-up endpoint and periodic
/// polling cover missed messages).
/// </remarks>
public interface IMobileSyncBroadcaster
{
    /// <summary>
    /// Append one change envelope to the per-request buffer. <see cref="FlushAsync"/>
    /// de-duplicates by <c>(Table, EntityId, Op, Audience)</c>, keeping the last envelope.
    /// </summary>
    public void Enqueue(MobileSyncChange change);

    /// <summary>
    /// Drain the buffer and broadcast each change. Called by the pipeline behavior after
    /// the command handler succeeds; idempotent within a scope.
    /// </summary>
    public Task FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Immediately broadcast a single change envelope, bypassing the buffer. Use from
    /// integration event handlers (post-commit by definition) or background jobs that
    /// are not wrapped by the pipeline behavior.
    /// </summary>
    public Task BroadcastNowAsync(MobileSyncChange change, CancellationToken cancellationToken = default);
}
