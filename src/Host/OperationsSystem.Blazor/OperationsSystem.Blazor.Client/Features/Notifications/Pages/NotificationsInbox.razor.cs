using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Localization;
using OperationsSystem.Blazor.Client.State;
using Radzen;

namespace OperationsSystem.Blazor.Client.Features.Notifications.Pages;

public partial class NotificationsInbox : IAsyncDisposable
{
    [Inject] private NotificationsApiClient Api { get; set; } = default!;
    [Inject] private NotificationCenterState Center { get; set; } = default!;
    [Inject] private LocaleState Locale { get; set; } = default!;
    [Inject] private UserTimeZone UserTimeZone { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private NotificationService Toasts { get; set; } = default!;
    [Inject] private DialogService Dialogs { get; set; } = default!;

    [Parameter, SupplyParameterFromQuery(Name = "category")]
    public string? Category { get; set; }

    private string selectedCategory = NotificationCategories.Invitations;
    private string? loadedCategory;
    private readonly CancellationTokenSource lifetimeCts = new();
    private CancellationTokenSource? visitCts;
    private NotificationInboxFeed? feed;
    private Virtualize<NotificationDto>? virtualList;
    private bool disposed;
    private bool isLoading = true;
    private bool loadError;
    private bool isUpdating;
    private long visitGeneration;
    private Guid? busyNotificationId;

    protected override async Task OnInitializedAsync()
    {
        Center.LiveReceived += OnLiveReceived;
        Center.Changed += OnCenterChanged;
        Center.Reconciled += OnCenterReconciled;
        await UserTimeZone.InitializeAsync();
    }

    protected override async Task OnParametersSetAsync()
    {
        var requestedCategory = NotificationCategories.All.Contains(Category, StringComparer.Ordinal)
            ? Category! : NotificationCategories.Invitations;
        if (loadedCategory == requestedCategory)
            return;
        loadedCategory = requestedCategory;
        selectedCategory = requestedCategory;
        visitGeneration++;
        visitCts?.Cancel();
        visitCts?.Dispose();
        visitCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
        virtualList = null;
        feed = new(Api, Center, selectedCategory);
        await LoadAsync();
    }

    private void SelectCategory(string category) => Navigation.NavigateTo($"/notifications?category={category}");

    private async Task LoadAsync()
    {
        if (disposed || feed is null || visitCts is null)
            return;
        var currentFeed = feed;
        var ct = visitCts.Token;
        isLoading = true;
        loadError = false;
        try
        {
            await Center.RetryPendingReadsAsync(ct);
            await currentFeed.LoadAsync(0, NotificationInboxFeed.PageSize, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception) { if (feed == currentFeed) loadError = true; }
        finally { if (feed == currentFeed) isLoading = false; }
    }

    private async ValueTask<ItemsProviderResult<NotificationDto>> ProvideItems(ItemsProviderRequest request)
    {
        var currentFeed = feed;
        if (currentFeed is null || visitCts is null)
            return new([], 0);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(request.CancellationToken, visitCts.Token);
        try
        {
            var slice = await currentFeed.LoadAsync(request.StartIndex, request.Count, linked.Token);
            if (feed != currentFeed)
                return new([], 0);
            await InvokeAsync(StateHasChanged);
            return new(slice.Items, slice.TotalCount);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { return new([], 0); }
        catch (Exception)
        {
            if (feed == currentFeed)
            {
                loadError = true;
                await InvokeAsync(StateHasChanged);
            }
            return new([], 0);
        }
    }

    private async Task OpenAsync(NotificationDto notification)
    {
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
        readCts.CancelAfter(TimeSpan.FromSeconds(5));
        if (!Center.Cache.IsReadLocally(notification))
        {
            try { await Center.MarkAsReadAsync(notification.Id, readCts.Token); }
            catch (Exception) { /* Category read is already queued if connectivity is unavailable. */ }
        }
        if (!disposed)
            Navigation.NavigateTo(NotificationPresentation.DeepLink(notification) ?? NotificationPresentation.InboxLink(notification));
    }

    private async Task ArchiveAsync(NotificationDto notification)
    {
        if (busyNotificationId is not null)
            return;
        busyNotificationId = notification.Id;
        try
        {
            await Center.ArchiveAsync(notification.Id, lifetimeCts.Token);
            await RefreshRowsAsync();
        }
        catch (Exception) { ShowActionFailure(); }
        finally { busyNotificationId = null; }
    }

    private async Task ArchiveAllAsync()
    {
        var confirmed = await Dialogs.Confirm(UiStrings.Notifications.ArchiveAllConfirm,
            UiStrings.Notifications.ArchiveAllTitle, new ConfirmOptions
            {
                OkButtonText = UiStrings.Notifications.ArchiveAll,
                CancelButtonText = UiStrings.Common.Cancel
            });
        if (confirmed is not true || disposed)
            return;
        isUpdating = true;
        try
        {
            await Center.ArchiveAllAsync(lifetimeCts.Token);
            await RefreshRowsAsync();
        }
        catch (Exception) { ShowActionFailure(); }
        finally { isUpdating = false; }
    }

    private async Task RefreshRowsAsync()
    {
        if (disposed || feed is null)
            return;
        if (!feed.HasCurrentSession)
        {
            feed = new(Api, Center, selectedCategory);
            virtualList = null;
        }
        feed.Invalidate();
        if (virtualList is not null)
            await virtualList.RefreshDataAsync();
        else
            await LoadAsync();
        StateHasChanged();
    }

    private string CategoryClass(string category) => selectedCategory == category
        ? "os-notifications-category os-notifications-category--active" : "os-notifications-category";

    private static string RelativeTime(DateTimeOffset createdAtUtc)
    {
        var elapsed = DateTimeOffset.UtcNow - createdAtUtc;
        if (elapsed < TimeSpan.FromMinutes(1)) return UiStrings.Common.JustNow;
        if (elapsed < TimeSpan.FromHours(1)) return string.Format(UiStrings.Common.MinutesAgo, Math.Max(1, (int)elapsed.TotalMinutes));
        if (elapsed < TimeSpan.FromDays(1)) return string.Format(UiStrings.Common.HoursAgo, Math.Max(1, (int)elapsed.TotalHours));
        return string.Format(UiStrings.Common.DaysAgo, Math.Max(1, (int)elapsed.TotalDays));
    }

    private void ShowActionFailure() => Toasts.Notify(NotificationSeverity.Error,
        UiStrings.Notifications.Title, UiStrings.Notifications.ActionFailed);

    private void OnLiveReceived(NotificationDto notification)
    {
        if (NotificationPresentation.Category(notification) == selectedCategory)
        {
            feed?.ObserveLive(notification);
            OnCenterReconciled();
        }
    }

    private int CategoryNewCount(string category) => category == selectedCategory && feed is not null
        ? feed.VisitNewCount : Center.GetCategoryUnreadCount(category);

    private async void OnCenterChanged()
    {
        try { await InvokeAsync(() => { if (!disposed) StateHasChanged(); }); }
        catch (Exception ex) { await DispatchExceptionAsync(ex); }
    }

    private async void OnCenterReconciled()
    {
        try { await InvokeAsync(RefreshRowsAsync); }
        catch (OperationCanceledException) when (disposed || visitCts?.IsCancellationRequested == true) { }
        catch (Exception ex) { await DispatchExceptionAsync(ex); }
    }

    public ValueTask DisposeAsync()
    {
        disposed = true;
        lifetimeCts.Cancel();
        visitCts?.Cancel();
        visitCts?.Dispose();
        lifetimeCts.Dispose();
        Center.LiveReceived -= OnLiveReceived;
        Center.Changed -= OnCenterChanged;
        Center.Reconciled -= OnCenterReconciled;
        return ValueTask.CompletedTask;
    }
}
