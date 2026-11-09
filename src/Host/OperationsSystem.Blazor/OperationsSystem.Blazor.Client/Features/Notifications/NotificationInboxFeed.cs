using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Features.Notifications;

/// <summary>One inbox visit: paged rows and the read receipts that preserve its initial new-row tint.</summary>
public sealed class NotificationInboxFeed(NotificationsApiClient api, NotificationCenterState center, string? category = null)
{
    public const int PageSize = 40;
    private readonly long sessionVersion = center.SessionVersion;
    public bool HasCurrentSession => center.IsInitialized && sessionVersion == center.SessionVersion;
    private const int MemoryPageLimit = 6;
    private readonly Dictionary<int, PagedResult<NotificationDto>> pages = [];
    private readonly HashSet<Guid> newIds = [];
    private readonly HashSet<Guid> acknowledgedIds = [];
    private readonly HashSet<DateTimeOffset> visitReceipts = [];
    private readonly SemaphoreSlim loadGate = new(1, 1);
    private bool acknowledged;
    private long revision;
    private int? visitNewCount;
    private DateTimeOffset? initialCutoff;
    public int VisitNewCount => visitNewCount ?? center.GetCategoryUnreadCount(category);
    public bool IsCached { get; private set; }
    public bool HasPendingRead => center.Cache.PendingReads.ContainsKey(category ?? NotificationInboxCache.InboxScope);
    public long TotalCount { get; private set; }

    public bool IsNew(NotificationDto row) => newIds.Contains(row.Id)
        || row.ReadAtUtc is { } readAt && visitReceipts.Contains(readAt)
        || !row.IsRead && !center.Cache.HasPendingRead(row);

    public void ObserveLive(NotificationDto notification)
    {
        if ((category is not null && NotificationPresentation.Category(notification) != category) || notification.IsRead || center.Cache.HasPendingRead(notification))
            return;
        if (newIds.Add(notification.Id) && visitNewCount is not null && notification.CreatedAtUtc > initialCutoff)
            visitNewCount++;
    }

    public void Invalidate()
    {
        revision++;
        pages.Clear();
    }

    public async Task<NotificationInboxSlice> LoadAsync(int startIndex, int count, CancellationToken ct)
    {
        EnsureCurrentSession();
        await loadGate.WaitAsync(ct);
        try
        {
            while (true)
            {
                EnsureCurrentSession();
                var requestRevision = revision;
                var result = await LoadCoreAsync(startIndex, count, ct);
                EnsureCurrentSession();
                if (requestRevision == revision)
                    return result;
                pages.Clear();
                ct.ThrowIfCancellationRequested();
            }
        }
        finally { loadGate.Release(); }
    }

    private async Task<NotificationInboxSlice> LoadCoreAsync(int startIndex, int count, CancellationToken ct)
    {
        var firstPage = startIndex / PageSize + 1;
        var lastPage = (startIndex + Math.Max(1, count) - 1) / PageSize + 1;
        var fetched = new List<NotificationDto>();
        IsCached = false;
        try
        {
            for (var page = firstPage; page <= lastPage; page++)
            {
                if (!pages.TryGetValue(page, out var result))
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    result = await api.GetInboxAsync(page, PageSize, ct: timeout.Token, category: category);
                    ct.ThrowIfCancellationRequested();
                    EnsureCurrentSession();
                    await center.Cache.StoreAsync(result.Items);
                    if (pages.Count >= MemoryPageLimit)
                        pages.Remove(pages.Keys.First());
                    pages[page] = result;
                }
                TotalCount = result.TotalCount;
                fetched.AddRange(result.Items);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested &&
            (ex is JSException or OperationCanceledException || ex is ApiException { StatusCode: >= 500 }))
        {
            var cached = center.Cache.GetCategory(category);
            IsCached = true;
            TotalCount = cached.Count;
            var slice = cached.Skip(startIndex).Take(count).ToArray();
            await AcknowledgeAsync(slice, ct, queueOnly: true);
            return new(slice, cached.Count);
        }
        var rows = fetched.Skip(startIndex % PageSize).Take(count).ToArray();
        await AcknowledgeAsync(rows, ct);
        return new(rows, (int)Math.Min(int.MaxValue, TotalCount));
    }

    private void EnsureCurrentSession()
    {
        if (!HasCurrentSession)
            throw new OperationCanceledException("The notification account changed.");
    }

    private async Task AcknowledgeAsync(IReadOnlyList<NotificationDto> rows, CancellationToken ct, bool queueOnly = false)
    {
        EnsureCurrentSession();
        if (rows.Count == 0)
            return;
        foreach (var row in rows.Where(row => newIds.Contains(row.Id) && row.ReadAtUtc is not null))
            visitReceipts.Add(row.ReadAtUtc!.Value);
        var unread = rows.Where(row => !row.IsRead && !center.Cache.HasPendingRead(row) && !newIds.Contains(row.Id)).ToArray();
        if (visitNewCount is null)
        {
            visitNewCount = Math.Max(center.GetCategoryUnreadCount(category), unread.Length);
            initialCutoff = rows.Max(row => row.CreatedAtUtc);
        }
        foreach (var row in unread)
        {
            if (newIds.Add(row.Id) && row.CreatedAtUtc > initialCutoff)
                visitNewCount++;
        }
        if (acknowledged && !rows.Any(row => !row.IsRead && !center.Cache.HasPendingRead(row) && !acknowledgedIds.Contains(row.Id)))
            return;
        acknowledged = true;
        if (queueOnly)
        {
            await center.QueueCategoryReadAsync(category, rows.Max(row => row.CreatedAtUtc));
            acknowledgedIds.UnionWith(rows.Select(row => row.Id));
            return;
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var receipt = category is null
                ? await center.MarkInboxAsReadAsync(rows.Max(row => row.CreatedAtUtc), timeout.Token)
                : await center.MarkCategoryAsReadAsync(category, rows.Max(row => row.CreatedAtUtc), timeout.Token);
            acknowledgedIds.UnionWith(rows.Select(row => row.Id));
            if (receipt is not null)
                visitReceipts.Add(receipt.ReadAtUtc);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The cutoff was persisted before the request, so a timed-out read can retry on reconnect.
        }
    }
}

public sealed record NotificationInboxSlice(IReadOnlyList<NotificationDto> Items, int TotalCount);
