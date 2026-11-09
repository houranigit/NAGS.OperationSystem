using System.Text.Json;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.Features.Notifications;
using OperationsSystem.Blazor.Client.State;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Notifications;

public sealed class NotificationInboxFeedTests
{
    [Fact]
    public async Task Whole_inbox_visit_reads_mixed_kinds_through_cutoff_and_preserves_older_new_tint()
    {
        var runtime = MixedInbox(85);
        var previouslyRead = runtime.Rows[70] with { IsRead = true, ReadAtUtc = DateTimeOffset.UtcNow.AddDays(-1) };
        runtime.Rows[70] = previouslyRead;
        var (api, center) = NewClient(runtime);
        await center.InitializeAsync(Guid.NewGuid());
        runtime.PauseRead = true;
        var feed = new NotificationInboxFeed(api, center);
        var loading = feed.LoadAsync(0, 40, default);
        await runtime.ReadStarted.Task;
        var cutoff = runtime.Rows[0].CreatedAtUtc;
        var newer = Row(-1) with { Kind = "FlightScheduleUpdated", CreatedAtUtc = cutoff.AddMinutes(1) };
        runtime.Rows.Insert(0, newer);
        center.ApplyLive(newer);
        runtime.ContinueRead.SetResult();

        var first = await loading;
        first.Items.Select(row => row.Kind).Distinct().Count().ShouldBe(4);
        first.Items.ShouldAllBe(row => feed.IsNew(row));
        center.UnreadCount.ShouldBe(1);
        runtime.Rows.Where(row => row.CreatedAtUtc <= cutoff).ShouldAllBe(row => row.IsRead);
        runtime.Rows.Single(row => row.Id == newer.Id).IsRead.ShouldBeFalse();
        runtime.Rows.Single(row => row.Id == previouslyRead.Id).ReadAtUtc.ShouldBe(previouslyRead.ReadAtUtc);

        var older = await feed.LoadAsync(40, 40, default);
        older.Items.Where(row => row.Id != previouslyRead.Id).ShouldAllBe(row => feed.IsNew(row));
        feed.IsNew(older.Items.Single(row => row.Id == previouslyRead.Id)).ShouldBeFalse();
        runtime.ReadCalls.ShouldBe(1);
        var nextVisit = new NotificationInboxFeed(api, center);
        (await nextVisit.LoadAsync(40, 40, default)).Items.ShouldAllBe(row => !nextVisit.IsNew(row));
    }

    [Fact]
    public async Task Whole_inbox_offline_read_replays_after_reload_without_reading_a_newer_notification()
    {
        var userId = Guid.NewGuid();
        var runtime = MixedInbox(70);
        var (_, initial) = NewClient(runtime);
        await initial.InitializeAsync(userId);
        var cutoff = runtime.Rows[0].CreatedAtUtc;
        runtime.Offline = true;
        var (api, center) = NewClient(runtime);
        await center.InitializeAsync(userId);
        var feed = new NotificationInboxFeed(api, center);

        var saved = await feed.LoadAsync(0, 40, default);
        saved.Items.Select(row => row.Kind).Distinct().Count().ShouldBe(4);
        saved.Items.ShouldAllBe(row => feed.IsNew(row));
        center.Cache.PendingReads.ShouldContainKey(NotificationInboxCache.InboxScope);
        center.UnreadCount.ShouldBe(0);
        runtime.AttemptedReadCalls.ShouldBe(0);

        var (restoredApi, restored) = NewClient(runtime);
        await restored.InitializeAsync(userId);
        var nextVisit = new NotificationInboxFeed(restoredApi, restored);
        (await nextVisit.LoadAsync(0, 40, default)).Items.ShouldAllBe(row => !nextVisit.IsNew(row));
        restored.Cache.PendingReads.Count.ShouldBe(1);
        var newer = Row(-1) with { Kind = "FlightScheduleUpdated", CreatedAtUtc = cutoff.AddMinutes(1) };
        runtime.Rows.Insert(0, newer);
        runtime.Offline = false;
        await restored.RetryPendingReadsAsync();
        await restored.RefreshAsync();

        restored.Cache.PendingReads.ShouldBeEmpty();
        restored.UnreadCount.ShouldBe(1);
        runtime.ReadCalls.ShouldBe(1);
        runtime.Rows.Where(row => row.CreatedAtUtc <= cutoff).ShouldAllBe(row => row.IsRead);
        runtime.Rows.Single(row => row.Id == newer.Id).IsRead.ShouldBeFalse();
    }

    [Fact]
    public async Task Category_visit_reads_older_unloaded_pages_and_retains_new_tint_until_next_visit()
    {
        var runtime = new InboxRuntime(85);
        var (api, center) = NewClient(runtime);
        await center.InitializeAsync(Guid.NewGuid());
        var feed = new NotificationInboxFeed(api, center, NotificationCategories.Invitations);

        var first = await feed.LoadAsync(0, 40, default);
        first.Items.Count.ShouldBe(40);
        first.TotalCount.ShouldBe(85);
        center.UnreadCount.ShouldBe(0);
        center.GetCategoryUnreadCount(NotificationCategories.Invitations).ShouldBe(0);
        feed.VisitNewCount.ShouldBe(85);
        first.Items.ShouldAllBe(row => feed.IsNew(row));
        runtime.ReadCalls.ShouldBe(1);
        var crossing = await feed.LoadAsync(35, 15, default);
        crossing.Items.Select(row => row.Id).ShouldBe(runtime.Rows.Skip(35).Take(15).Select(row => row.Id));
        crossing.Items.ShouldAllBe(row => feed.IsNew(row));
        runtime.ReadCalls.ShouldBe(1);

        var nextVisit = new NotificationInboxFeed(api, center, NotificationCategories.Invitations);
        var later = await nextVisit.LoadAsync(40, 40, default);
        later.Items.ShouldAllBe(row => !nextVisit.IsNew(row));
    }

    [Fact]
    public async Task Cached_inbox_and_pending_read_survive_reload_and_retry_when_connected()
    {
        var userId = Guid.NewGuid();
        var runtime = new InboxRuntime(70);
        var (_, initial) = NewClient(runtime);
        await initial.InitializeAsync(userId);
        runtime.Offline = true;
        var (api, center) = NewClient(runtime);
        await center.InitializeAsync(userId);
        var feed = new NotificationInboxFeed(api, center, NotificationCategories.Invitations);

        var saved = await feed.LoadAsync(0, 40, default);
        feed.IsCached.ShouldBeTrue();
        runtime.AttemptedReadCalls.ShouldBe(0);
        saved.Items.Count.ShouldBe(40);
        saved.Items.ShouldAllBe(row => feed.IsNew(row));
        center.Cache.PendingReads.ShouldContainKey(NotificationCategories.Invitations);
        center.UnreadCount.ShouldBe(0);

        var (reloadedApi, reloaded) = NewClient(runtime);
        await reloaded.InitializeAsync(userId);
        var nextVisit = new NotificationInboxFeed(reloadedApi, reloaded, NotificationCategories.Invitations);
        var restored = await nextVisit.LoadAsync(0, 40, default);
        restored.Items.ShouldAllBe(row => !nextVisit.IsNew(row));
        reloaded.Cache.PendingReads.Count.ShouldBe(1);
        reloaded.UnreadCount.ShouldBe(0);
        runtime.Offline = false;
        await reloaded.RetryPendingReadsAsync();
        await reloaded.RefreshAsync();
        reloaded.Cache.PendingReads.ShouldBeEmpty();
        reloaded.UnreadCount.ShouldBe(0);
        runtime.Rows.ShouldAllBe(row => row.IsRead);
    }

    [Fact]
    public async Task Cache_is_bounded_per_category_and_cleared_on_logout_or_account_change()
    {
        var runtime = new InboxRuntime(0);
        var cache = new NotificationInboxCache(runtime);
        var userId = Guid.NewGuid();
        await cache.InitializeAsync(userId, "https://portal.example/api/v1");
        var rows = Enumerable.Range(0, 260).Select(index => Row(index)).ToArray();
        await cache.StoreAsync(rows.Concat(Enumerable.Range(300, 30).Select(index => Row(index) with { Kind = "FlightReminder" })), 290);
        cache.GetCategory(NotificationCategories.Invitations).Count.ShouldBe(NotificationInboxCache.CategoryLimit);
        cache.GetCategory(NotificationCategories.Reminders).Count.ShouldBe(30);
        var restored = new NotificationInboxCache(runtime);
        await restored.InitializeAsync(userId, "https://portal.example/api/v1");
        restored.GetCategory(NotificationCategories.Invitations).Count.ShouldBe(200);
        await restored.InitializeAsync(Guid.NewGuid(), "https://portal.example/api/v1");
        restored.GetCategory(NotificationCategories.Invitations).ShouldBeEmpty();
        runtime.Storage.ShouldBeEmpty();
        await restored.StoreAsync([Row(1)]);
        await restored.ClearAsync();
        runtime.Storage.ShouldBeEmpty();
    }

    [Fact]
    public async Task Category_read_does_not_overwrite_live_rows_or_a_newer_global_count()
    {
        var runtime = new InboxRuntime(1) { PauseRead = true };
        var (_, center) = NewClient(runtime);
        await center.InitializeAsync(Guid.NewGuid());
        var cutoff = runtime.Rows[0].CreatedAtUtc;
        var read = center.MarkCategoryAsReadAsync(NotificationCategories.Invitations, cutoff);
        await runtime.ReadStarted.Task;
        var late = Row(-1) with { CreatedAtUtc = cutoff.AddMinutes(1) };
        runtime.Rows.Insert(0, late);
        center.ApplyLive(late);
        runtime.ContinueRead.SetResult();
        await read;

        center.Recent.Single(row => row.Id == late.Id).IsRead.ShouldBeFalse();
        center.UnreadCount.ShouldBe(1);
        center.Cache.GetCategory(NotificationCategories.Invitations).Single(row => row.Id == late.Id).IsRead.ShouldBeFalse();
    }

    [Fact]
    public async Task Live_invalidation_during_a_page_fetch_retries_the_stale_page()
    {
        var runtime = new InboxRuntime(60) { PauseInbox = true };
        var (api, center) = NewClient(runtime);
        await center.InitializeAsync(Guid.NewGuid());
        var feed = new NotificationInboxFeed(api, center, NotificationCategories.Invitations);
        var loading = feed.LoadAsync(0, 40, default);
        await runtime.InboxStarted.Task;
        var live = Row(-1);
        runtime.Rows.Insert(0, live);
        center.ApplyLive(live);
        feed.Invalidate();
        runtime.ContinueInbox.SetResult();
        var result = await loading;
        result.Items.First().Id.ShouldBe(live.Id);
        result.TotalCount.ShouldBe(61);
    }

    [Fact]
    public async Task Category_intent_is_saved_even_when_a_busy_mutation_gate_is_canceled()
    {
        var runtime = new InboxRuntime(1) { PauseRead = true };
        var reminder = Row(10) with { Kind = "FlightReminder" };
        runtime.Rows.Add(reminder);
        var (_, center) = NewClient(runtime);
        await center.InitializeAsync(Guid.NewGuid());
        var first = center.MarkCategoryAsReadAsync(NotificationCategories.Invitations, runtime.Rows[0].CreatedAtUtc);
        await runtime.ReadStarted.Task;
        using var cts = new CancellationTokenSource();
        var waiting = center.MarkCategoryAsReadAsync(NotificationCategories.Reminders, reminder.CreatedAtUtc, cts.Token);
        cts.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => waiting);
        center.Cache.PendingReads.ShouldContainKey(NotificationCategories.Reminders);
        runtime.ContinueRead.SetResult();
        await first;
        center.Cache.PendingReads.ShouldContainKey(NotificationCategories.Reminders);
    }

    [Fact]
    public async Task Inflight_feed_cannot_store_old_account_rows_after_account_change()
    {
        var runtime = new InboxRuntime(2) { PauseInbox = true };
        var (api, center) = NewClient(runtime);
        await center.InitializeAsync(Guid.NewGuid());
        var feed = new NotificationInboxFeed(api, center, NotificationCategories.Invitations);
        var loading = feed.LoadAsync(0, 40, default);
        await runtime.InboxStarted.Task;
        runtime.Rows.Clear();
        await center.ClearAsync();
        await center.InitializeAsync(Guid.NewGuid());
        runtime.ContinueInbox.SetResult();
        await Should.ThrowAsync<OperationCanceledException>(() => loading);
        center.Cache.GetCategory(NotificationCategories.Invitations).ShouldBeEmpty();
        runtime.ReadCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Successful_individual_read_is_retained_in_the_offline_cache_and_badge()
    {
        var userId = Guid.NewGuid();
        var runtime = new InboxRuntime(2);
        var (_, center) = NewClient(runtime);
        await center.InitializeAsync(userId);
        var id = runtime.Rows[0].Id;
        await center.MarkAsReadAsync(id);
        runtime.Offline = true;
        var (_, restored) = NewClient(runtime);
        await restored.InitializeAsync(userId);
        restored.UnreadCount.ShouldBe(1);
        restored.Cache.GetCategory(NotificationCategories.Invitations).Single(row => row.Id == id).IsRead.ShouldBeTrue();
    }

    [Fact]
    public async Task Active_visit_retains_initial_count_and_counts_a_new_live_row_once_while_other_tabs_stay_unread()
    {
        var runtime = new InboxRuntime(10);
        var (api, center) = NewClient(runtime);
        await center.InitializeAsync(Guid.NewGuid());
        var feed = new NotificationInboxFeed(api, center, NotificationCategories.Invitations);
        await feed.LoadAsync(0, 40, default);
        feed.VisitNewCount.ShouldBe(10);
        center.GetCategoryUnreadCount(NotificationCategories.Invitations).ShouldBe(0);
        var live = Row(-1) with { CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1) };
        runtime.Rows.Insert(0, live);
        center.ApplyLive(live).ShouldBeTrue();
        feed.ObserveLive(live);
        feed.ObserveLive(live);
        feed.VisitNewCount.ShouldBe(11);
        center.GetCategoryUnreadCount(NotificationCategories.Invitations).ShouldBe(1);
        feed.Invalidate();
        await feed.LoadAsync(0, 40, default);
        await center.RefreshAsync();
        feed.Invalidate();
        var refreshed = await feed.LoadAsync(0, 40, default);
        feed.VisitNewCount.ShouldBe(11);
        refreshed.Items.ShouldAllBe(row => feed.IsNew(row));
        center.GetCategoryUnreadCount(NotificationCategories.Invitations).ShouldBe(0);
        var reminder = Row(0) with { Kind = "FlightReminder" };
        runtime.Rows.Add(reminder);
        center.ApplyLive(reminder);
        feed.ObserveLive(reminder);
        feed.VisitNewCount.ShouldBe(11);
        center.GetCategoryUnreadCount(NotificationCategories.Reminders).ShouldBe(1);
        var nextVisit = new NotificationInboxFeed(api, center, NotificationCategories.Invitations);
        var seen = await nextVisit.LoadAsync(0, 40, default);
        nextVisit.VisitNewCount.ShouldBe(0);
        seen.Items.ShouldAllBe(row => !nextVisit.IsNew(row));
    }

    [Fact]
    public async Task Category_counts_are_authoritative_beyond_cached_pages_and_persist_for_offline_restore()
    {
        var user = Guid.NewGuid();
        var runtime = new InboxRuntime(240);
        runtime.Rows.AddRange(Enumerable.Range(0, 125).Select(index => Row(index) with { Kind = "FlightReminder" }));
        runtime.Rows.Add(Row(0) with { Kind = "LegacyFlightKind" });
        var (_, center) = NewClient(runtime);
        await center.InitializeAsync(user);
        center.GetCategoryUnreadCount(NotificationCategories.Invitations).ShouldBe(240);
        center.GetCategoryUnreadCount(NotificationCategories.Reminders).ShouldBe(125);
        center.GetCategoryUnreadCount(NotificationCategories.FlightUpdates).ShouldBe(1);
        center.Cache.GetCategory(NotificationCategories.Invitations).Count.ShouldBeLessThan(240);
        runtime.Offline = true;
        var (_, restored) = NewClient(runtime);
        await restored.InitializeAsync(user);
        restored.GetCategoryUnreadCount(NotificationCategories.Invitations).ShouldBe(240);
        restored.GetCategoryUnreadCount(NotificationCategories.Reminders).ShouldBe(125);
        restored.GetCategoryUnreadCount(NotificationCategories.FlightUpdates).ShouldBe(1);
    }

    private static InboxRuntime MixedInbox(int count)
    {
        var runtime = new InboxRuntime(count);
        string[] kinds = ["EmployeeInvitedToFlight", "FlightReminder", "StaffAssignedToFlight", "FlightScheduleUpdated"];
        for (var index = 0; index < runtime.Rows.Count; index++)
            runtime.Rows[index] = runtime.Rows[index] with { Kind = kinds[index % kinds.Length] };
        return runtime;
    }

    private static NotificationDto Row(int index) => new(Guid.NewGuid(), "EmployeeInvitedToFlight", "Flight invitation", "Invitation details",
        "دعوة رحلة", "تفاصيل الدعوة", new Dictionary<string, string>(), false, DateTimeOffset.UtcNow.AddMinutes(-10-index), null);

    private static (NotificationsApiClient Api, NotificationCenterState Center) NewClient(IJSRuntime runtime)
    {
        var tokens = new AuthTokenStore();
        var locale = new LocaleState(runtime);
        var refresher = new ClientTokenRefresher(runtime, tokens, locale);
        var api = new NotificationsApiClient(new BrowserApiClient(runtime, tokens, locale, refresher));
        return (api, new NotificationCenterState(api, runtime));
    }

    private sealed class InboxRuntime(int count) : IJSRuntime
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
        public List<NotificationDto> Rows { get; } = Enumerable.Range(0, count).Select(Row).ToList();
        public Dictionary<string, string> Storage { get; } = [];
        public bool Offline { get; set; }
        public bool PauseRead { get; set; }
        public bool PauseInbox { get; set; }
        public TaskCompletionSource InboxStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueInbox { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ReadCalls { get; private set; }
        public int AttemptedReadCalls { get; private set; }
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private UnreadNotificationCount Counts() => new(Rows.Count(row => !row.IsRead),
            Rows.Count(row => !row.IsRead && NotificationPresentation.Category(row) == NotificationCategories.Invitations),
            Rows.Count(row => !row.IsRead && NotificationPresentation.Category(row) == NotificationCategories.Reminders),
            Rows.Count(row => !row.IsRead && NotificationPresentation.Category(row) == NotificationCategories.FlightUpdates));
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
        {
            if (identifier == "operationsSystem.storage.get")
                return Storage.TryGetValue((string)args![0]!, out var saved) ? (TValue)(object)saved : default!;
            if (identifier == "operationsSystem.storage.set")
                Storage[(string)args![0]!] = (string)args[1]!;
            if (identifier == "operationsSystem.storage.remove")
                Storage.Remove((string)args![0]!);
            if (identifier != "operationsSystem.api.request")
                return default!;
            var path = (string)args![1]!;
            if (path is "/notifications/me/read-category" or "/notifications/me/read-inbox")
                AttemptedReadCalls++;
            if (Offline)
                throw new JSException("Failed to fetch");
            object response;
            if (path is "/notifications/me/read-category" or "/notifications/me/read-inbox")
            {
                ReadCalls++;
                var request = path == "/notifications/me/read-category" ? (MarkNotificationCategoryReadRequest)args[2]! : null;
                var cutoff = request?.ThroughCreatedAtUtc ?? JsonSerializer.SerializeToElement(args[2], Options).GetProperty("throughCreatedAtUtc").GetDateTimeOffset();
                var affected = Rows.Where(row => (request is null || NotificationPresentation.Category(row) == request.Category) && row.CreatedAtUtc <= cutoff && !row.IsRead).Select(row => row.Id).ToHashSet();
                if (PauseRead)
                {
                    ReadStarted.TrySetResult();
                    await ContinueRead.Task.WaitAsync(ct);
                }
                var readAt = DateTimeOffset.UtcNow;
                for (var i = 0; i < Rows.Count; i++)
                    if (affected.Contains(Rows[i].Id))
                        Rows[i] = Rows[i] with { IsRead = true, ReadAtUtc = readAt };
                var counts = Counts();
                response = new NotificationCategoryReadDto(readAt, PauseRead ? 0 : counts.Count, counts.Invitations, counts.Reminders, counts.FlightUpdates);
            }
            else if (path.EndsWith("/read", StringComparison.Ordinal))
            {
                var id = Guid.Parse(path.Split('/')[2]);
                var index = Rows.FindIndex(row => row.Id == id);
                Rows[index] = Rows[index] with { IsRead = true, ReadAtUtc = DateTimeOffset.UtcNow };
                response = new { };
            }
            else if (path.EndsWith("unread-count", StringComparison.Ordinal))
                response = Counts();
            else
            {
                var query = new Uri("https://portal.example" + path).Query.TrimStart('?').Split('&')
                    .Select(part => part.Split('=')).ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1]));
                var page = int.Parse(query["page"]);
                var size = int.Parse(query["pageSize"]);
                var filtered = Rows.Where(row => !query.TryGetValue("category", out var category) || NotificationPresentation.Category(row) == category).ToArray();
                response = new PagedResult<NotificationDto>(filtered.Skip((page-1)*size).Take(size).ToArray(), page, size, filtered.Length);
                if (PauseInbox && query.ContainsKey("category"))
                {
                    PauseInbox = false;
                    InboxStarted.TrySetResult();
                    await ContinueInbox.Task.WaitAsync(ct);
                }
            }
            return (TValue)(object)JsonSerializer.Serialize(response, Options);
        }
    }
}
