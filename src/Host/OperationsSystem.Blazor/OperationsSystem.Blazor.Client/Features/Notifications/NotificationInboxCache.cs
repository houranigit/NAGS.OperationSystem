using System.Text.Json;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Features.Notifications;

/// <summary>Bounded, account-scoped browser copy of fetched inbox rows and unsent read cutoffs.</summary>
public sealed class NotificationInboxCache(IJSRuntime jsRuntime)
{
    public const int CategoryLimit = 200;
    public const string InboxScope = "Inbox";
    private const string Prefix = "os:notifications:inbox:v1:";
    private readonly SemaphoreSlim storageGate = new(1, 1);
    private string? storageKey;
    private Dictionary<Guid, NotificationDto> rows = [];
    private Dictionary<string, DateTimeOffset> pending = [];
    private Dictionary<string, int> categoryCounts = [];
    public int? UnreadCount { get; private set; }
    public IReadOnlyDictionary<string, int> CategoryCounts => categoryCounts;
    public IReadOnlyDictionary<string, DateTimeOffset> PendingReads => new Dictionary<string, DateTimeOffset>(pending);

    public async Task InitializeAsync(Guid userId, string apiEnvironment, CancellationToken ct = default)
    {
        var key = Prefix + Uri.EscapeDataString(apiEnvironment) + ":" + userId;
        if (storageKey == key)
            return;
        await ClearAsync();
        storageKey = key;
        try
        {
            var json = await jsRuntime.InvokeAsync<string?>("operationsSystem.storage.get", ct, key);
            if (storageKey != key || string.IsNullOrWhiteSpace(json))
                return;
            var saved = JsonSerializer.Deserialize<SavedInbox>(json);
            if (saved is null)
                return;
            rows = saved.Items.GroupBy(row => row.Id).Select(group => group.First()).ToDictionary(row => row.Id);
            pending = saved.PendingReads.Where(pair => pair.Key == InboxScope || NotificationCategories.All.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            UnreadCount = saved.UnreadCount;
            categoryCounts = saved.CategoryCounts?.Where(pair => NotificationCategories.All.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => Math.Max(0, pair.Value)) ?? [];
            Trim();
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or JsonException)
        {
            // Storage availability does not prevent using the online inbox.
        }
    }

    public IReadOnlyList<NotificationDto> GetCategory(string? category) => rows.Values
        .Where(row => category is null || NotificationPresentation.Category(row) == category)
        .OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id).ToArray();

    public bool IsReadLocally(NotificationDto row) => row.IsRead
        || rows.TryGetValue(row.Id, out var cached) && cached.IsRead || HasPendingRead(row);

    public bool HasPendingRead(NotificationDto row) => pending.Any(pair =>
        (pair.Key == InboxScope || pair.Key == NotificationPresentation.Category(row)) && row.CreatedAtUtc <= pair.Value);

    public async Task StoreAsync(IEnumerable<NotificationDto> fetched, int? unreadCount = null, IReadOnlyDictionary<string, int>? counts = null)
    {
        if (storageKey is null)
            return;
        foreach (var row in fetched)
            rows[row.Id] = row;
        if (unreadCount is not null)
            UnreadCount = unreadCount;
        if (counts is not null)
            categoryCounts = counts.ToDictionary(pair => pair.Key, pair => pair.Value);
        Trim();
        await SaveAsync();
    }

    public async Task QueueReadAsync(string? category, DateTimeOffset cutoff)
    {
        if (storageKey is null)
            return;
        var newlySeen = rows.Values.Where(row => (category is null || NotificationPresentation.Category(row) == category)
            && row.CreatedAtUtc <= cutoff && !row.IsRead && !HasPendingRead(row)).ToArray();
        var key = category ?? InboxScope;
        if (!pending.TryGetValue(key, out var previous) || previous < cutoff)
            pending[key] = cutoff;
        if (UnreadCount is { } count)
            UnreadCount = Math.Max(0, count - newlySeen.Length);
        foreach (var group in newlySeen.GroupBy(NotificationPresentation.Category))
            if (categoryCounts.TryGetValue(group.Key, out var categoryCount))
                categoryCounts[group.Key] = Math.Max(0, categoryCount - group.Count());
        await SaveAsync();
    }

    public async Task AcknowledgeAsync(string? category, DateTimeOffset cutoff, NotificationCategoryReadDto receipt,
        IReadOnlySet<Guid> eligibleIds)
    {
        foreach (var id in eligibleIds)
            if (rows.TryGetValue(id, out var row) && !row.IsRead)
                rows[id] = row with { IsRead = true, ReadAtUtc = receipt.ReadAtUtc };
        if (pending.TryGetValue(category ?? InboxScope, out var queued) && queued <= cutoff)
            pending.Remove(category ?? InboxScope);
        UnreadCount = receipt.UnreadCount;
        categoryCounts = NotificationCategories.All.Where(key => receipt.Counts.ForCategory(key) is not null)
            .ToDictionary(key => key, key => Math.Max(0, receipt.Counts.ForCategory(key)!.Value));
        await SaveAsync();
    }

    public async Task MarkReadAsync(Guid id, DateTimeOffset readAt)
    {
        if (rows.TryGetValue(id, out var row) && !row.IsRead)
            rows[id] = row with { IsRead = true, ReadAtUtc = readAt };
        await SaveAsync();
    }

    public async Task RemoveAsync(Guid id)
    {
        rows.Remove(id);
        await SaveAsync();
    }

    public async Task RemoveManyAsync(IEnumerable<Guid> ids)
    {
        foreach (var id in ids)
            rows.Remove(id);
        await SaveAsync();
    }

    public async Task ClearAsync()
    {
        var key = storageKey;
        storageKey = null;
        rows.Clear();
        pending.Clear();
        UnreadCount = null;
        categoryCounts.Clear();
        if (key is null)
            return;
        await storageGate.WaitAsync();
        try { await jsRuntime.InvokeVoidAsync("operationsSystem.storage.remove", key); }
        catch (Exception ex) when (ex is JSException or InvalidOperationException) { }
        finally { storageGate.Release(); }
    }

    private void Trim() => rows = rows.Values.GroupBy(NotificationPresentation.Category)
        .SelectMany(group => group.OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id).Take(CategoryLimit))
        .ToDictionary(row => row.Id);

    private async Task SaveAsync()
    {
        var key = storageKey;
        if (key is null)
            return;
        await storageGate.WaitAsync();
        try
        {
            if (storageKey != key)
                return;
            var json = JsonSerializer.Serialize(new SavedInbox(rows.Values.ToArray(), new(pending), UnreadCount, new(categoryCounts)));
            await jsRuntime.InvokeVoidAsync("operationsSystem.storage.set", key, json);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException) { }
        finally { storageGate.Release(); }
    }

    public sealed record SavedInbox(NotificationDto[] Items, Dictionary<string, DateTimeOffset> PendingReads, int? UnreadCount, Dictionary<string, int>? CategoryCounts = null);
}
