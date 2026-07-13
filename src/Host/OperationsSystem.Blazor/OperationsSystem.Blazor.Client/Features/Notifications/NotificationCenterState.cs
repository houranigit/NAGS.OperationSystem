using System.Text.Json;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Features.Notifications;

/// <summary>
/// Per-browser/circuit notification state shared by the shell bell and inbox. REST remains the
/// authority; SignalR adds new records opportunistically and is deduplicated by notification id.
/// </summary>
public sealed class NotificationCenterState(NotificationsApiClient api, IJSRuntime jsRuntime)
{
    public const int RecentPageSize = 8;
    private const int ReconciliationPageSize = 100;
    private const int PersistedSeenLimit = 256;
    private const string SeenStoragePrefix = "os:notifications:seen:";

    private readonly SemaphoreSlim initializeGate = new(1, 1);
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly SemaphoreSlim mutationGate = new(1, 1);
    private readonly SemaphoreSlim signalReconciliationGate = new(1, 1);
    private readonly HashSet<Guid> seenIds = [];
    private IReadOnlyList<NotificationDto> recent = [];
    private Guid? userId;
    private long stateRevision;
    private int signalReconciliationRequested;

    public IReadOnlyList<NotificationDto> Recent => recent;
    public int UnreadCount { get; private set; }
    public bool IsLoading { get; private set; }
    public bool HasError { get; private set; }
    public bool IsInitialized => userId is not null;

    public event Action? Changed;
    public event Action<NotificationDto>? LiveReceived;
    public event Action? Reconciled;

    public async Task InitializeAsync(Guid authenticatedUserId, CancellationToken cancellationToken = default)
    {
        await initializeGate.WaitAsync(cancellationToken);
        try
        {
            if (userId == authenticatedUserId && IsInitialized)
                return;

            userId = authenticatedUserId;
            recent = [];
            seenIds.Clear();
            stateRevision++;
            await LoadSeenIdsAsync(authenticatedUserId, cancellationToken);
            await RefreshAsync(cancellationToken);
        }
        finally
        {
            initializeGate.Release();
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await refreshGate.WaitAsync(cancellationToken);
        var operationUserId = userId;
        if (operationUserId is null)
        {
            refreshGate.Release();
            return;
        }

        IsLoading = true;
        HasError = false;
        RaiseChanged();

        try
        {
            while (userId == operationUserId)
            {
                var requestRevision = stateRevision;

                // Reconcile a wider window than the bell displays so a stable-id delivery retry
                // cannot create a second toast merely because the record fell outside eight rows.
                var inboxTask = api.GetInboxAsync(1, ReconciliationPageSize, ct: cancellationToken);
                var countTask = api.GetUnreadCountAsync(cancellationToken);
                await Task.WhenAll(inboxTask, countTask);

                if (userId != operationUserId)
                    return;

                // A toast or user action landed while REST was in flight. Discard these stale
                // snapshots and re-read after that change instead of erasing the live record.
                if (stateRevision != requestRevision)
                    continue;

                var inbox = await inboxTask;
                recent = inbox.Items
                    .OrderByDescending(item => item.CreatedAtUtc)
                    .Take(RecentPageSize)
                    .ToArray();
                foreach (var item in inbox.Items)
                    seenIds.Add(item.Id);
                UnreadCount = Math.Max(0, (await countTask).Count);
                stateRevision++;
                _ = PersistSeenIdsAsync(operationUserId.Value);
                Reconciled?.Invoke();
                break;
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (userId == operationUserId)
                HasError = true;
        }
        finally
        {
            if (userId == operationUserId)
            {
                IsLoading = false;
                RaiseChanged();
            }

            refreshGate.Release();
        }
    }

    /// <returns><see langword="true"/> when this is a new live record and a toast should be shown.</returns>
    public bool ApplyLive(NotificationDto notification)
    {
        if (userId is null || !seenIds.Add(notification.Id))
            return false;

        recent = recent
            .Prepend(notification)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(RecentPageSize)
            .ToArray();

        if (!notification.IsRead)
            UnreadCount++;

        HasError = false;
        stateRevision++;
        _ = PersistSeenIdsAsync(userId.Value);
        RaiseChanged();
        LiveReceived?.Invoke(notification);
        return true;
    }

    /// <summary>
    /// Re-reads the authoritative inbox after a SignalR delivery. This is intentionally invoked for
    /// duplicate deliveries too: the REST list and unread-count endpoints are independent reads, so
    /// a notification can be present in one snapshot but not the other. Coalescing prevents a burst
    /// of hub messages from producing one pair of REST calls per message while guaranteeing that a
    /// request arriving during an active reconciliation causes one final pass.
    /// </summary>
    public async Task ReconcileAfterSignalAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref signalReconciliationRequested, 1);
        await signalReconciliationGate.WaitAsync(cancellationToken);
        try
        {
            while (Interlocked.Exchange(ref signalReconciliationRequested, 0) == 1)
                await RefreshAsync(cancellationToken);
        }
        finally
        {
            signalReconciliationGate.Release();
        }
    }

    public async Task MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            var operationUserId = userId;
            var existing = recent.FirstOrDefault(item => item.Id == notificationId);
            if (existing?.IsRead == true)
                return;

            await api.MarkAsReadAsync(notificationId, cancellationToken);
            if (operationUserId is null || operationUserId != userId)
                return;

            if (existing is not null)
            {
                var readAt = DateTimeOffset.UtcNow;
                recent = recent.Select(item => item.Id == notificationId
                    ? item with { IsRead = true, ReadAtUtc = item.ReadAtUtc ?? readAt }
                    : item).ToArray();
            }

            UnreadCount = Math.Max(0, UnreadCount - 1);
            stateRevision++;
            RaiseChanged();
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public async Task MarkAllAsReadAsync(CancellationToken cancellationToken = default)
    {
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            var operationUserId = userId;
            var operationRevision = stateRevision;
            await api.MarkAllAsReadAsync(cancellationToken);
            if (operationUserId is null || operationUserId != userId)
                return;

            if (stateRevision != operationRevision)
            {
                await RefreshAsync(cancellationToken);
                return;
            }

            var readAt = DateTimeOffset.UtcNow;
            recent = recent.Select(item => item.IsRead
                ? item
                : item with { IsRead = true, ReadAtUtc = item.ReadAtUtc ?? readAt }).ToArray();
            UnreadCount = 0;
            stateRevision++;
            RaiseChanged();
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public async Task ArchiveAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            var operationUserId = userId;
            await api.ArchiveAsync(notificationId, cancellationToken);
            if (operationUserId is null || operationUserId != userId)
                return;

            // The inbox can archive records outside the bell's recent window. Re-read the
            // authoritative count instead of guessing whether that record was unread.
            await RefreshAsync(cancellationToken);
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public async Task ArchiveAllAsync(CancellationToken cancellationToken = default)
    {
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            var operationUserId = userId;
            var operationRevision = stateRevision;
            await api.ArchiveAllAsync(cancellationToken);
            if (operationUserId is null || operationUserId != userId)
                return;

            if (stateRevision != operationRevision)
            {
                await RefreshAsync(cancellationToken);
                return;
            }

            recent = [];
            UnreadCount = 0;
            stateRevision++;
            RaiseChanged();
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public void Clear()
    {
        userId = null;
        recent = [];
        seenIds.Clear();
        UnreadCount = 0;
        IsLoading = false;
        HasError = false;
        stateRevision++;
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke();

    private async Task LoadSeenIdsAsync(Guid authenticatedUserId, CancellationToken cancellationToken)
    {
        try
        {
            var json = await jsRuntime.InvokeAsync<string?>(
                "operationsSystem.storage.get",
                cancellationToken,
                SeenStoragePrefix + authenticatedUserId);
            if (string.IsNullOrWhiteSpace(json))
                return;

            foreach (var id in JsonSerializer.Deserialize<Guid[]>(json) ?? [])
                seenIds.Add(id);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or JsonException)
        {
            // Browser storage is an optimization; REST reconciliation remains authoritative.
        }
    }

    private async Task PersistSeenIdsAsync(Guid authenticatedUserId)
    {
        try
        {
            var json = JsonSerializer.Serialize(seenIds.TakeLast(PersistedSeenLimit));
            await jsRuntime.InvokeVoidAsync(
                "operationsSystem.storage.set",
                SeenStoragePrefix + authenticatedUserId,
                json);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            // Best-effort deduplication across reloads.
        }
    }
}
