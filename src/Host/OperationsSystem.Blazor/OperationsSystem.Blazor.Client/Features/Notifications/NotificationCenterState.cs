using System.Text.Json;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Features.Notifications;

/// <summary>
/// Per-browser/circuit notification state shared by the shell bell and inbox. REST remains the
/// authority; SignalR adds new records opportunistically and is deduplicated by notification id.
/// </summary>
public sealed class NotificationCenterState(NotificationsApiClient api, IJSRuntime jsRuntime, NavigationManager? navigation = null)
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
    private Dictionary<string, int> categoryUnreadCounts = [];

    public NotificationInboxCache Cache { get; } = new(jsRuntime);
    public long SessionVersion { get; private set; }

    public int GetCategoryUnreadCount(string category) => categoryUnreadCounts.TryGetValue(category, out var count)
        ? count : Cache.GetCategory(category).Count(row => !Cache.IsReadLocally(row));

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

            SessionVersion++;
            userId = authenticatedUserId;
            recent = [];
            seenIds.Clear();
            stateRevision++;
            await Cache.InitializeAsync(authenticatedUserId,
                navigation is null ? "/api/v1" : new Uri(navigation.BaseUri).GetLeftPart(UriPartial.Authority) + "/api/v1",
                cancellationToken);
            recent = NotificationCategories.All.SelectMany(Cache.GetCategory)
                .OrderByDescending(row => row.CreatedAtUtc).Take(RecentPageSize).ToArray();
            UnreadCount = Cache.UnreadCount ?? 0;
            categoryUnreadCounts = Cache.CategoryCounts.ToDictionary(pair => pair.Key, pair => pair.Value);
            await LoadSeenIdsAsync(authenticatedUserId, cancellationToken);
            await RetryPendingReadsAsync(cancellationToken);
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
                try
                {
                    await Task.WhenAll(inboxTask, countTask);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    if (userId != operationUserId)
                        return;
                    if (stateRevision != requestRevision)
                        continue;

                    // A list failure must not hide a successfully fetched global badge count.
                    if (countTask.IsCompletedSuccessfully)
                    {
                        ApplyUnreadCounts(await countTask);
                        stateRevision++;
                        await Cache.StoreAsync([], UnreadCount, categoryUnreadCounts);
                    }
                    HasError = true;
                    break;
                }

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
                ApplyUnreadCounts(await countTask);
                stateRevision++;
                await Cache.StoreAsync(inbox.Items, UnreadCount, categoryUnreadCounts);
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
        {
            UnreadCount++;
            var category = NotificationPresentation.Category(notification);
            if (categoryUnreadCounts.TryGetValue(category, out var count))
                categoryUnreadCounts[category] = count + 1;
        }

        HasError = false;
        stateRevision++;
        _ = Cache.StoreAsync([notification], UnreadCount, categoryUnreadCounts);
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

    public async Task QueueCategoryReadAsync(string category, DateTimeOffset throughCreatedAtUtc)
    {
        var operationSession = SessionVersion;
        if (userId is null)
            return;
        await Cache.QueueReadAsync(category, throughCreatedAtUtc);
        if (operationSession != SessionVersion)
            return;
        UnreadCount = Cache.UnreadCount ?? UnreadCount;
        categoryUnreadCounts = Cache.CategoryCounts.ToDictionary(pair => pair.Key, pair => pair.Value);
        stateRevision++;
        RaiseChanged();
    }

    /// <summary>Opening a category acknowledges only records no newer than the observed server cutoff.</summary>
    public async Task<NotificationCategoryReadDto?> MarkCategoryAsReadAsync(
        string category, DateTimeOffset throughCreatedAtUtc, CancellationToken cancellationToken = default)
    {
        var operationSession = SessionVersion;
        var operationUserId = userId;
        if (operationUserId is null)
            return null;
        var eligibleIds = Cache.GetCategory(category).Concat(recent)
            .Where(row => NotificationPresentation.Category(row) == category && row.CreatedAtUtc <= throughCreatedAtUtc && !row.IsRead)
            .Select(row => row.Id).ToHashSet();
        // Persist the intent before a busy mutation gate or network timeout can interrupt it.
        await QueueCategoryReadAsync(category, throughCreatedAtUtc);
        if (operationSession != SessionVersion)
            return null;
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            if (operationSession != SessionVersion)
                return null;
            var revision = stateRevision;
            NotificationCategoryReadDto receipt;
            try
            {
                receipt = await api.MarkCategoryAsReadAsync(category, throughCreatedAtUtc, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
                (ex is JSException || ex is ApiException { StatusCode: >= 500 }))
            {
                // Keep the persisted cutoff and known-row badge estimate until connectivity returns.
                return null;
            }
            if (operationSession != SessionVersion)
                return null;
            recent = recent.Select(row => eligibleIds.Contains(row.Id) && !row.IsRead
                ? row with { IsRead = true, ReadAtUtc = receipt.ReadAtUtc } : row).ToArray();
            var needsReconciliation = stateRevision != revision;
            if (!needsReconciliation)
                ApplyUnreadCounts(receipt.Counts);
            stateRevision++;
            await Cache.AcknowledgeAsync(category, throughCreatedAtUtc, receipt, eligibleIds);
            RaiseChanged();
            if (needsReconciliation)
                await RefreshUnreadCountAsync(cancellationToken);
            await Cache.StoreAsync([], UnreadCount, categoryUnreadCounts);
            return receipt;
        }
        finally { mutationGate.Release(); }
    }

    public async Task RetryPendingReadsAsync(CancellationToken cancellationToken = default)
    {
        var operationSession = SessionVersion;
        foreach (var (category, cutoff) in Cache.PendingReads)
        {
            if (operationSession != SessionVersion)
                return;
            if (await MarkCategoryAsReadAsync(category, cutoff, cancellationToken) is null)
                break;
        }
    }

    public async Task MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        var operationSession = SessionVersion;
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            if (operationSession != SessionVersion)
                return;
            var operationUserId = userId;
            if (operationUserId is null)
                return;
            var existing = recent.FirstOrDefault(item => item.Id == notificationId);
            if (existing?.IsRead == true)
                return;

            await api.MarkAsReadAsync(notificationId, cancellationToken);
            if (operationSession != SessionVersion)
                return;

            var readAt = DateTimeOffset.UtcNow;
            recent = recent.Select(item => item.Id == notificationId
                ? item with { IsRead = true, ReadAtUtc = item.ReadAtUtc ?? readAt }
                : item).ToArray();
            stateRevision++;
            await Cache.MarkReadAsync(notificationId, readAt);
            RaiseChanged();
            await RefreshUnreadCountAsync(cancellationToken);
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public async Task MarkAllAsReadAsync(CancellationToken cancellationToken = default)
    {
        var operationSession = SessionVersion;
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            if (operationSession != SessionVersion)
                return;
            var operationUserId = userId;
            if (operationUserId is null)
                return;
            await api.MarkAllAsReadAsync(cancellationToken);
            if (operationSession != SessionVersion)
                return;

            // Read the server's result; a new live row may have arrived after the bulk action began.
            await RefreshAsync(cancellationToken);
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public async Task ArchiveAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        var operationSession = SessionVersion;
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            if (operationSession != SessionVersion)
                return;
            var operationUserId = userId;
            if (operationUserId is null)
                return;
            await api.ArchiveAsync(notificationId, cancellationToken);
            if (operationSession != SessionVersion)
                return;

            await Cache.RemoveAsync(notificationId);

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
        var operationSession = SessionVersion;
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            if (operationSession != SessionVersion)
                return;
            var operationUserId = userId;
            if (operationUserId is null)
                return;
            var affectedIds = NotificationCategories.All.SelectMany(Cache.GetCategory).Select(row => row.Id).ToArray();
            await api.ArchiveAllAsync(cancellationToken);
            if (operationSession != SessionVersion)
                return;

            await Cache.RemoveManyAsync(affectedIds);
            // Read the server's result; a new live row may have arrived after the bulk action began.
            await RefreshAsync(cancellationToken);
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public async Task ClearAsync()
    {
        Clear();
        await Cache.ClearAsync();
    }

    public void Clear()
    {
        SessionVersion++;
        userId = null;
        recent = [];
        seenIds.Clear();
        UnreadCount = 0;
        categoryUnreadCounts.Clear();
        IsLoading = false;
        HasError = false;
        stateRevision++;
        RaiseChanged();
    }

    private void ApplyUnreadCounts(UnreadNotificationCount counts)
    {
        UnreadCount = Math.Max(0, counts.Count);
        categoryUnreadCounts = NotificationCategories.All.Where(category => counts.ForCategory(category) is not null)
            .ToDictionary(category => category, category => Math.Max(0, counts.ForCategory(category)!.Value));
    }

    private void RaiseChanged() => Changed?.Invoke();

    private async Task RefreshUnreadCountAsync(CancellationToken cancellationToken)
    {
        await refreshGate.WaitAsync(cancellationToken);
        var operationUserId = userId;
        try
        {
            while (operationUserId is not null && userId == operationUserId)
            {
                var requestRevision = stateRevision;
                var result = await api.GetUnreadCountAsync(cancellationToken);
                if (userId != operationUserId)
                    return;
                if (stateRevision != requestRevision)
                    continue;

                ApplyUnreadCounts(result);
                stateRevision++;
                await Cache.StoreAsync([], UnreadCount, categoryUnreadCounts);
                RaiseChanged();
                break;
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The read succeeded. Keep its read state and reconcile the badge on the next refresh.
            if (userId == operationUserId)
            {
                HasError = true;
                RaiseChanged();
            }
        }
        finally
        {
            refreshGate.Release();
        }
    }

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
