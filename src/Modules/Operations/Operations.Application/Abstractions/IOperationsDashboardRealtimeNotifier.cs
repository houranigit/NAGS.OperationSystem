namespace Operations.Application.Abstractions;

/// <summary>
/// Publishes a payload-free invalidation for the operations analytics dashboard.
/// Persisted REST projections remain authoritative; connected clients re-query them when notified.
/// </summary>
public interface IOperationsDashboardRealtimeNotifier
{
    public Task NotifyChangedAsync(CancellationToken cancellationToken = default);
}
