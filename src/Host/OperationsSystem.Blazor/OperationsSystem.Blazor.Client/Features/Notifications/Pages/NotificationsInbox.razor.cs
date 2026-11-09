using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Globalization;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.State;
using Radzen;

namespace OperationsSystem.Blazor.Client.Features.Notifications.Pages;

public partial class NotificationsInbox : IAsyncDisposable
{
    [Inject] private NotificationsApiClient Api { get; set; } = default!;
    [Inject] private NotificationCenterState Center { get; set; } = default!;
    [Inject] private UserTimeZone UserTimeZone { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private NotificationService Toasts { get; set; } = default!;
    [Inject] private DialogService Dialogs { get; set; } = default!;
    [Inject] private NotificationTargetAvailability Targets { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private readonly CancellationTokenSource lifetimeCts = new();
    private readonly SemaphoreSlim loadGate = new(1, 1);
    private readonly List<NotificationDto> rows = [];
    private List<NotificationGroup> groups = [];
    private NotificationInboxFeed? feed;
    private InboxScrollInterop? scroll;
    private DotNetObjectReference<NotificationsInbox>? self;
    private ElementReference scrollElement;
    private ElementReference endSentinel;
    private bool disposed;
    private bool isLoading = true;
    private bool loadError;
    private bool isUpdating;
    private bool isLoadingMore;
    private bool pagingError;
    private int nextIndex;
    private Guid? busyNotificationId;
    private int rowVersion;

    protected override async Task OnInitializedAsync()
    {
        Center.LiveReceived += OnLiveReceived;
        Center.Changed += OnCenterChanged;
        Center.Reconciled += OnCenterReconciled;
        await UserTimeZone.InitializeAsync();
        feed = new(Api, Center);
        await LoadAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (disposed || isLoading || loadError || rows.Count == 0)
            return;
        scroll ??= new(JS);
        self ??= DotNetObjectReference.Create(this);
        await scroll.UpdateAsync(scrollElement, endSentinel, self,
            !isLoadingMore && !pagingError && nextIndex < feed?.TotalCount, rows.Count);
    }

    private async Task LoadAsync()
    {
        if (disposed || feed is null)
            return;
        isLoading = true;
        loadError = false;
        pagingError = false;
        try
        {
            await Center.RetryPendingReadsAsync(lifetimeCts.Token);
            await RefreshFirstPageAsync();
        }
        catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested) { }
        catch (Exception) { loadError = true; }
        finally { isLoading = false; }
    }

    private async Task RefreshFirstPageAsync()
    {
        await loadGate.WaitAsync(lifetimeCts.Token);
        try
        {
            if (disposed || feed is null)
                return;
            if (!feed.HasCurrentSession)
            {
                feed = new(Api, Center);
                rows.Clear();
                groups.Clear();
                nextIndex = 0;
                if (!Center.IsInitialized)
                    return;
            }
            feed.Invalidate();
            var slice = await feed.LoadAsync(0, NotificationInboxFeed.PageSize, lifetimeCts.Token);
            if (busyNotificationId is not null)
                return;
            var freshIds = slice.Items.Select(row => row.Id).ToHashSet();
            // A live first-page refresh keeps already fetched older history in the same visit.
            var merged = slice.Items.Concat(rows.Where(row => !freshIds.Contains(row.Id)))
                .OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id).ToArray();
            rows.Clear();
            rows.AddRange(merged.Take(slice.TotalCount));
            nextIndex = rows.Count;
            await Targets.CheckAsync(rows, lifetimeCts.Token);
            RebuildGroups();
        }
        finally { loadGate.Release(); }
    }

    [JSInvokable]
    public async Task LoadOlderAsync()
    {
        await InvokeAsync(async () =>
        {
            if (disposed || isLoading || isLoadingMore || pagingError || feed is null || nextIndex >= feed.TotalCount)
                return;
            isLoadingMore = true;
            StateHasChanged();
            await loadGate.WaitAsync(lifetimeCts.Token);
            try
            {
                var slice = await feed.LoadAsync(nextIndex, NotificationInboxFeed.PageSize, lifetimeCts.Token);
                nextIndex += slice.Items.Count;
                var ids = rows.Select(row => row.Id).ToHashSet();
                rows.AddRange(slice.Items.Where(row => ids.Add(row.Id)));
                if (slice.Items.Count == 0)
                    nextIndex = slice.TotalCount;
                await Targets.CheckAsync(slice.Items, lifetimeCts.Token);
                RebuildGroups();
            }
            catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested) { }
            catch (Exception) { pagingError = true; }
            finally
            {
                loadGate.Release();
                isLoadingMore = false;
                if (!disposed) StateHasChanged();
            }
        });
    }

    private async Task RetryOlderAsync()
    {
        pagingError = false;
        await LoadOlderAsync();
    }

    private void RebuildGroups()
    {
        var today = DateOnly.FromDateTime(UserTimeZone.ToLocalDateTime(DateTimeOffset.UtcNow));
        groups = rows.OrderByDescending(row => feed?.IsNew(row) == true)
            .ThenByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id)
            .GroupBy(row => feed?.IsNew(row) == true ? "new"
                : DateOnly.FromDateTime(UserTimeZone.ToLocalDateTime(row.CreatedAtUtc)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Select(group => new NotificationGroup(group.Key, group.Key == "new" ? "New"
                : NotificationPresentation.DateHeading(DateOnly.ParseExact(group.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture), today), group.ToList()))
            .ToList();
    }

    private async Task OpenAsync(NotificationDto notification)
    {
        try { await Targets.CheckAsync([notification], lifetimeCts.Token, force: true); }
        catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested) { return; }
        if (!Targets.IsOpenable(notification))
        {
            // Refresh each virtualized group's items so its cached row fragment drops Open too.
            rowVersion++;
            RebuildGroups();
            StateHasChanged();
            return;
        }
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
        readCts.CancelAfter(TimeSpan.FromSeconds(5));
        if (!Center.Cache.IsReadLocally(notification))
        {
            try { await Center.MarkAsReadAsync(notification.Id, readCts.Token); }
            catch (Exception) { /* The inbox read cutoff remains queued while offline. */ }
        }
        if (!disposed)
            Navigation.NavigateTo(NotificationPresentation.DeepLink(notification) ?? NotificationPresentation.InboxLink(notification));
    }

    private async Task BeginDeleteAsync(Guid notificationId)
    {
        scroll ??= new(JS);
        await scroll.DeleteAsync(notificationId);
    }

    [JSInvokable]
    public async Task<bool> DeleteNotificationAsync(Guid notificationId)
    {
        var success = false;
        await InvokeAsync(async () =>
        {
            if (disposed || busyNotificationId is not null || !rows.Any(row => row.Id == notificationId))
                return;
            var currentFeed = feed;
            busyNotificationId = notificationId;
            StateHasChanged();
            try
            {
                await Center.ArchiveAsync(notificationId, lifetimeCts.Token);
                success = !disposed && feed == currentFeed && currentFeed?.HasCurrentSession == true;
            }
            catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested) { }
            catch (Exception) { ShowActionFailure(); }
            finally
            {
                if (!success) busyNotificationId = null;
                if (!disposed) StateHasChanged();
            }
        });
        return success;
    }

    [JSInvokable]
    public async Task CompleteNotificationDeleteAsync(Guid notificationId)
    {
        await InvokeAsync(async () =>
        {
            if (disposed || busyNotificationId != notificationId)
                return;
            rows.RemoveAll(row => row.Id == notificationId);
            busyNotificationId = null;
            RebuildGroups();
            StateHasChanged();
            try { await RefreshRowsAsync(); }
            catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested) { }
            catch (Exception) { pagingError = true; StateHasChanged(); }
        });
    }

    private async Task ArchiveAllAsync()
    {
        var confirmed = await Dialogs.Confirm("Archived notifications remain in history.",
            "Delete all notifications?", new ConfirmOptions { OkButtonText = "Delete all", CancelButtonText = "Cancel" });
        if (confirmed is not true || disposed)
            return;
        isUpdating = true;
        try
        {
            await Center.ArchiveAllAsync(lifetimeCts.Token);
            rows.Clear();
            await RefreshRowsAsync();
        }
        catch (Exception) { ShowActionFailure(); }
        finally { isUpdating = false; }
    }

    private async Task RefreshRowsAsync()
    {
        if (disposed || feed is null)
            return;
        await RefreshFirstPageAsync();
        if (!disposed) StateHasChanged();
    }

    private void ShowActionFailure() => Toasts.Notify(NotificationSeverity.Error,
        "Notifications", "The notification could not be updated. Please try again.");

    private void OnLiveReceived(NotificationDto notification)
    {
        feed?.ObserveLive(notification);
        OnCenterReconciled();
    }

    private async void OnCenterChanged()
    {
        try { await InvokeAsync(() => { if (!disposed) StateHasChanged(); }); }
        catch (Exception ex) { await DispatchExceptionAsync(ex); }
    }

    private async void OnCenterReconciled()
    {
        // Keep the keyed row in place until its successful removal animation completes.
        if (busyNotificationId is not null)
            return;
        try { await InvokeAsync(RefreshRowsAsync); }
        catch (OperationCanceledException) when (disposed || lifetimeCts.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!disposed)
                await InvokeAsync(() => { pagingError = true; StateHasChanged(); });
        }
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        Center.LiveReceived -= OnLiveReceived;
        Center.Changed -= OnCenterChanged;
        Center.Reconciled -= OnCenterReconciled;
        lifetimeCts.Cancel();
        if (scroll is not null) await scroll.DisposeAsync();
        self?.Dispose();
        lifetimeCts.Dispose();
    }

    private sealed record NotificationGroup(string Key, string Heading, List<NotificationDto> Items);

    private sealed class InboxScrollInterop(IJSRuntime js) : IAsyncDisposable
    {
        private const string ModulePath = "./Features/Notifications/Pages/NotificationsInbox.razor.js";
        private const string UpdateMethod = "update";
        private const string DisposeMethod = "dispose";
        private const string DeleteMethod = "deleteNotification";
        private IJSObjectReference? module;

        public async ValueTask UpdateAsync(ElementReference root, ElementReference sentinel,
            DotNetObjectReference<NotificationsInbox> receiver, bool enabled, int rowCount)
        {
            module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            await module.InvokeVoidAsync(UpdateMethod, root, sentinel, receiver, enabled, rowCount);
        }

        public async ValueTask DeleteAsync(Guid notificationId)
        {
            module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            await module.InvokeVoidAsync(DeleteMethod, notificationId);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (module is not null)
                {
                    await module.InvokeVoidAsync(DisposeMethod);
                    await module.DisposeAsync();
                }
            }
            catch (JSDisconnectedException) { }
        }
    }
}
