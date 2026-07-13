using System.Text.Json;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.Features.Notifications;
using OperationsSystem.Blazor.Client.State;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Notifications;

public sealed class NotificationCenterStateTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task State_loads_applies_live_once_and_keeps_unread_count_consistent()
    {
        var existing = Notification(isRead: false);
        var runtime = new ApiJsRuntime(path => path switch
        {
            "/notifications/me?page=1&pageSize=100&unreadOnly=false" => JsonSerializer.Serialize(
                new PagedResult<NotificationDto>([existing], 1, 8, 1), JsonOptions),
            "/notifications/me/unread-count" => JsonSerializer.Serialize(new UnreadNotificationCount(1), JsonOptions),
            _ => string.Empty
        });
        var state = NewState(runtime);

        await state.InitializeAsync(Guid.NewGuid());

        state.Recent.Count.ShouldBe(1);
        state.Recent[0].Id.ShouldBe(existing.Id);
        state.UnreadCount.ShouldBe(1);
        state.HasError.ShouldBeFalse();

        var live = Notification(isRead: false);
        state.ApplyLive(live).ShouldBeTrue();
        state.ApplyLive(live).ShouldBeFalse();
        state.Recent.First().ShouldBe(live);
        state.UnreadCount.ShouldBe(2);

        await state.MarkAsReadAsync(live.Id);

        state.Recent.First(item => item.Id == live.Id).IsRead.ShouldBeTrue();
        state.UnreadCount.ShouldBe(1);
        runtime.Requests.ShouldContain(request => request.Method == "POST" && request.Path == $"/notifications/{live.Id}/read");

        await state.ArchiveAllAsync();

        state.Recent.ShouldBeEmpty();
        state.UnreadCount.ShouldBe(0);
        runtime.Requests.ShouldContain(request => request.Path == "/notifications/me/archive-all");
    }

    [Fact]
    public async Task Typed_client_uses_the_notifications_contract_routes()
    {
        var runtime = new ApiJsRuntime(path => path switch
        {
            "/notifications/me?page=2&pageSize=12&unreadOnly=true" => JsonSerializer.Serialize(
                new PagedResult<NotificationDto>([], 2, 12, 0), JsonOptions),
            "/notifications/me/unread-count" => JsonSerializer.Serialize(new UnreadNotificationCount(0), JsonOptions),
            _ => string.Empty
        });
        var client = NewClient(runtime);
        var id = Guid.NewGuid();

        await client.GetInboxAsync(2, 12, true);
        await client.GetUnreadCountAsync();
        await client.MarkAsReadAsync(id);
        await client.MarkAllAsReadAsync();
        await client.ArchiveAsync(id);
        await client.ArchiveAllAsync();

        runtime.Requests.Select(request => (request.Method, request.Path)).ShouldBe(
        [
            ("GET", "/notifications/me?page=2&pageSize=12&unreadOnly=true"),
            ("GET", "/notifications/me/unread-count"),
            ("POST", $"/notifications/{id}/read"),
            ("POST", "/notifications/me/mark-all-read"),
            ("POST", $"/notifications/{id}/archive"),
            ("POST", "/notifications/me/archive-all")
        ]);
    }

    [Fact]
    public async Task Refresh_retries_instead_of_overwriting_a_notification_received_while_in_flight()
    {
        var existing = Notification(isRead: false);
        var live = Notification(isRead: false);
        var runtime = new ControlledRefreshJsRuntime();
        var state = NewState(runtime);

        var initialize = state.InitializeAsync(Guid.NewGuid());
        await runtime.WaitForRoundAsync(0);

        state.ApplyLive(live).ShouldBeTrue();
        runtime.CompleteRound(0, [existing], unreadCount: 1);

        await runtime.WaitForRoundAsync(1);
        runtime.CompleteRound(1, [live, existing], unreadCount: 2);
        await initialize;

        state.Recent.Select(item => item.Id).ShouldContain(live.Id);
        state.UnreadCount.ShouldBe(2);
        runtime.InboxCalls.ShouldBe(2);
    }

    [Fact]
    public async Task Reconciliation_after_a_deduplicated_signal_repairs_list_count_snapshot_skew()
    {
        var existing = Notification(isRead: false);
        var liveAlreadyPresentInList = Notification(isRead: false);
        var runtime = new ControlledRefreshJsRuntime();
        var state = NewState(runtime);

        var initialize = state.InitializeAsync(Guid.NewGuid());
        await runtime.WaitForRoundAsync(0);
        // Simulate the inbox snapshot seeing the committed row while the independent unread-count
        // snapshot was taken just before it committed.
        runtime.CompleteRound(0, [liveAlreadyPresentInList, existing], unreadCount: 1);
        await initialize;

        state.UnreadCount.ShouldBe(1);
        state.ApplyLive(liveAlreadyPresentInList).ShouldBeFalse();

        var reconcile = state.ReconcileAfterSignalAsync();
        await runtime.WaitForRoundAsync(1);
        runtime.CompleteRound(1, [liveAlreadyPresentInList, existing], unreadCount: 2);
        await reconcile;

        state.UnreadCount.ShouldBe(2);
        state.Recent.Select(item => item.Id).ShouldContain(liveAlreadyPresentInList.Id);
        runtime.InboxCalls.ShouldBe(2);
    }

    private static NotificationCenterState NewState(IJSRuntime runtime) => new(NewClient(runtime), runtime);

    private static NotificationsApiClient NewClient(IJSRuntime runtime)
    {
        var tokenStore = new AuthTokenStore();
        var locale = new LocaleState(runtime);
        var refresher = new ClientTokenRefresher(runtime, tokenStore, locale);
        return new NotificationsApiClient(new BrowserApiClient(runtime, tokenStore, locale, refresher));
    }

    private static NotificationDto Notification(bool isRead) => new(
        Guid.NewGuid(),
        "StaffAssignedToFlight",
        "Assigned",
        "A teammate added you.",
        "تم تعيينك",
        "أضافك أحد الزملاء.",
        new Dictionary<string, string> { ["flightId"] = Guid.NewGuid().ToString() },
        isRead,
        DateTimeOffset.UtcNow,
        isRead ? DateTimeOffset.UtcNow : null);

    private sealed class ApiJsRuntime(Func<string, string> responseForPath) : IJSRuntime
    {
        public List<ApiRequest> Requests { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier != "operationsSystem.api.request")
                return ValueTask.FromResult(default(TValue)!);

            var method = (string)args![0]!;
            var path = (string)args[1]!;
            Requests.Add(new ApiRequest(method, path));
            return ValueTask.FromResult((TValue)(object)responseForPath(path));
        }
    }

    private sealed class ControlledRefreshJsRuntime : IJSRuntime
    {
        private readonly TaskCompletionSource[] roundStarted =
        [
            new(TaskCreationOptions.RunContinuationsAsynchronously),
            new(TaskCreationOptions.RunContinuationsAsynchronously)
        ];
        private readonly TaskCompletionSource<string>[] inboxResponses =
        [
            new(TaskCreationOptions.RunContinuationsAsynchronously),
            new(TaskCreationOptions.RunContinuationsAsynchronously)
        ];
        private readonly TaskCompletionSource<string>[] countResponses =
        [
            new(TaskCreationOptions.RunContinuationsAsynchronously),
            new(TaskCreationOptions.RunContinuationsAsynchronously)
        ];
        private readonly int[] requestsInRound = new int[2];
        private int inboxCalls;
        private int countCalls;

        public int InboxCalls => inboxCalls;

        public Task WaitForRoundAsync(int round) => roundStarted[round].Task;

        public void CompleteRound(int round, IReadOnlyList<NotificationDto> items, int unreadCount)
        {
            inboxResponses[round].TrySetResult(JsonSerializer.Serialize(
                new PagedResult<NotificationDto>(items, 1, 100, items.Count), JsonOptions));
            countResponses[round].TrySetResult(JsonSerializer.Serialize(
                new UnreadNotificationCount(unreadCount), JsonOptions));
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            if (identifier != "operationsSystem.api.request")
                return default!;

            var path = (string)args![1]!;
            Task<string> response;
            int round;
            if (path.StartsWith("/notifications/me?page=", StringComparison.Ordinal))
            {
                round = Interlocked.Increment(ref inboxCalls) - 1;
                response = inboxResponses[round].Task;
            }
            else if (path == "/notifications/me/unread-count")
            {
                round = Interlocked.Increment(ref countCalls) - 1;
                response = countResponses[round].Task;
            }
            else
            {
                return (TValue)(object)string.Empty;
            }

            if (Interlocked.Increment(ref requestsInRound[round]) == 2)
                roundStarted[round].TrySetResult();
            var json = await response.WaitAsync(cancellationToken);
            return (TValue)(object)json;
        }
    }

    private sealed record ApiRequest(string Method, string Path);
}
