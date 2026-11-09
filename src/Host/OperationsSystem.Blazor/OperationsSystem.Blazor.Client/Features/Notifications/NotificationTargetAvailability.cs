using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Features.Notifications;

/// <summary>Availability belongs to Operations and is checked without downloading flight details.</summary>
public sealed class NotificationTargetAvailability
{
    private readonly Func<IReadOnlyList<Guid>, CancellationToken, Task<IReadOnlyList<Guid>>> fetch;
    private readonly Func<long> sessionVersion;
    private readonly Func<bool> hasSession;
    private readonly TimeProvider time;
    private readonly Dictionary<Guid, DateTimeOffset> checkedAt = [];
    private readonly HashSet<Guid> openable = [];
    private readonly Dictionary<Guid, long> requests = [];
    private long requestRevision;
    private long version = -1;

    public NotificationTargetAvailability(OperationsApiClient api, NotificationCenterState center)
        : this(api.GetNotificationTargetsAsync, () => center.SessionVersion, () => center.IsInitialized, TimeProvider.System) { }

    internal NotificationTargetAvailability(Func<IReadOnlyList<Guid>, CancellationToken, Task<IReadOnlyList<Guid>>> fetch,
        Func<long> sessionVersion, Func<bool> hasSession, TimeProvider time)
    {
        this.fetch = fetch;
        this.sessionVersion = sessionVersion;
        this.hasSession = hasSession;
        this.time = time;
    }

    public bool IsOpenable(NotificationDto row) => CurrentSession()
        && row.Kind != "FlightScheduleUpdated"
        && NotificationPresentation.TryGetFlightId(row, out var id) && openable.Contains(id);

    public async Task CheckAsync(IEnumerable<NotificationDto> rows, CancellationToken ct = default, bool force = false)
    {
        if (!CurrentSession()) return;
        var requestedVersion = version;
        var now = time.GetUtcNow();
        var ids = rows.Where(row => row.Kind != "FlightScheduleUpdated")
            .Select(row => NotificationPresentation.TryGetFlightId(row, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty).Distinct()
            .Where(id => force || !checkedAt.TryGetValue(id, out var stamp) || now - stamp >= TimeSpan.FromSeconds(30))
            .ToArray();
        foreach (var batch in ids.Chunk(100))
        {
            var revision = ++requestRevision;
            foreach (var id in batch) { openable.Remove(id); requests[id] = revision; }
            IReadOnlyList<Guid> available;
            var succeeded = true;
            try { available = await fetch(batch, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { available = []; succeeded = false; } // Unknown availability keeps Open hidden.
            if (!CurrentSession() || version != requestedVersion) return;
            var allowed = batch.Where(id => requests.GetValueOrDefault(id) == revision).ToHashSet();
            openable.UnionWith(available.Where(allowed.Contains));
            foreach (var id in allowed)
            {
                if (succeeded) checkedAt[id] = now;
                else checkedAt.Remove(id);
            }
        }
    }

    private bool CurrentSession()
    {
        var current = sessionVersion();
        if (version != current || !hasSession())
        {
            checkedAt.Clear();
            openable.Clear();
            requests.Clear();
            version = current;
        }
        return hasSession();
    }
}
