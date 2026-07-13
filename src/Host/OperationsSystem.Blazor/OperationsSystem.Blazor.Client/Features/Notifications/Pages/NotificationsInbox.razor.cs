using System.Globalization;
using Microsoft.AspNetCore.Components;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Localization;
using OperationsSystem.Blazor.Client.State;
using Radzen;

namespace OperationsSystem.Blazor.Client.Features.Notifications.Pages;

public partial class NotificationsInbox : IAsyncDisposable
{
    private const int PageSize = 12;

    [Inject] private NotificationsApiClient Api { get; set; } = default!;
    [Inject] private NotificationCenterState Center { get; set; } = default!;
    [Inject] private LocaleState Locale { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private NotificationService Toasts { get; set; } = default!;
    [Inject] private DialogService Dialogs { get; set; } = default!;

    private IReadOnlyList<NotificationDto> items = [];
    private int page = 1;
    private int totalPages;
    private long totalCount;
    private bool unreadOnly;
    private bool isLoading = true;
    private bool loadError;
    private bool isUpdating;
    private Guid? busyNotificationId;
    private long loadGeneration;
    private long liveRevision;
    private readonly Dictionary<Guid, PendingLiveNotification> pendingLive = [];

    protected override async Task OnInitializedAsync()
    {
        Center.LiveReceived += OnLiveReceived;
        Center.Reconciled += OnCenterReconciled;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var generation = ++loadGeneration;
        var requestedPage = page;
        var requestedUnreadOnly = unreadOnly;
        var requestStartLiveRevision = liveRevision;
        isLoading = true;
        loadError = false;

        try
        {
            var result = await Api.GetInboxAsync(requestedPage, PageSize, requestedUnreadOnly);
            if (generation != loadGeneration)
                return;

            var resultTotalPages = Math.Max(1, result.TotalPages);
            if (requestedPage > resultTotalPages)
            {
                requestedPage = resultTotalPages;
                page = requestedPage;
                result = await Api.GetInboxAsync(requestedPage, PageSize, requestedUnreadOnly);
                if (generation != loadGeneration)
                    return;
            }

            ApplyLoadedPage(result, requestedPage, requestedUnreadOnly, requestStartLiveRevision);
        }
        catch (Exception)
        {
            if (generation == loadGeneration)
                loadError = true;
        }
        finally
        {
            if (generation == loadGeneration)
                isLoading = false;
        }
    }

    private void ApplyLoadedPage(
        PagedResult<NotificationDto> result,
        int requestedPage,
        bool requestedUnreadOnly,
        long requestStartLiveRevision)
    {
        var merged = NotificationInboxReconciler.Merge(
            result,
            pendingLive,
            requestStartLiveRevision,
            requestedPage,
            requestedUnreadOnly,
            PageSize);

        foreach (var id in merged.ConsumedPendingIds)
            pendingLive.Remove(id);

        items = merged.Items;
        totalCount = merged.TotalCount;
        totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        page = requestedPage;
    }

    private async Task SetUnreadOnlyAsync(bool value)
    {
        if (unreadOnly == value)
            return;

        unreadOnly = value;
        page = 1;
        await LoadAsync();
    }

    private async Task OpenAsync(NotificationDto notification)
    {
        if (!notification.IsRead)
            await MarkReadAsync(notification, showFailure: false);

        Navigation.NavigateTo(NotificationPresentation.DeepLink(notification) ?? "/notifications");
    }

    private async Task MarkReadAsync(NotificationDto notification, bool showFailure = true)
    {
        if (notification.IsRead || busyNotificationId is not null)
            return;

        busyNotificationId = notification.Id;
        try
        {
            await Center.MarkAsReadAsync(notification.Id);
            var readAt = DateTimeOffset.UtcNow;
            items = items.Select(item => item.Id == notification.Id
                ? item with { IsRead = true, ReadAtUtc = item.ReadAtUtc ?? readAt }
                : item).ToArray();
            if (pendingLive.TryGetValue(notification.Id, out var pending))
            {
                pendingLive[notification.Id] = pending with
                {
                    Notification = pending.Notification with
                    {
                        IsRead = true,
                        ReadAtUtc = pending.Notification.ReadAtUtc ?? readAt
                    }
                };
            }

            if (unreadOnly)
            {
                items = items.Where(item => item.Id != notification.Id).ToArray();
                totalCount = Math.Max(0, totalCount - 1);
                await FillPageIfNeededAsync();
            }
        }
        catch (Exception)
        {
            if (showFailure)
                ShowActionFailure();
        }
        finally
        {
            busyNotificationId = null;
        }
    }

    private async Task MarkAllReadAsync()
    {
        isUpdating = true;
        try
        {
            await Center.MarkAllAsReadAsync();
            pendingLive.Clear();
            await LoadAsync();
        }
        catch (Exception)
        {
            ShowActionFailure();
        }
        finally
        {
            isUpdating = false;
        }
    }

    private async Task ArchiveAsync(NotificationDto notification)
    {
        if (busyNotificationId is not null)
            return;

        busyNotificationId = notification.Id;
        try
        {
            await Center.ArchiveAsync(notification.Id);
            pendingLive.Remove(notification.Id);
            items = items.Where(item => item.Id != notification.Id).ToArray();
            totalCount = Math.Max(0, totalCount - 1);
            await FillPageIfNeededAsync();
        }
        catch (Exception)
        {
            ShowActionFailure();
        }
        finally
        {
            busyNotificationId = null;
        }
    }

    private async Task ArchiveAllAsync()
    {
        var confirmed = await Dialogs.Confirm(
            UiStrings.Notifications.ArchiveAllConfirm,
            UiStrings.Notifications.ArchiveAllTitle,
            new ConfirmOptions
            {
                OkButtonText = UiStrings.Notifications.ArchiveAll,
                CancelButtonText = UiStrings.Common.Cancel
            });

        if (confirmed is not true)
            return;

        isUpdating = true;
        try
        {
            await Center.ArchiveAllAsync();
            page = 1;
            pendingLive.Clear();
            await LoadAsync();
        }
        catch (Exception)
        {
            ShowActionFailure();
        }
        finally
        {
            isUpdating = false;
        }
    }

    private async Task FillPageIfNeededAsync()
    {
        totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        if (items.Count == 0 && totalCount > 0)
        {
            page = Math.Min(page, totalPages);
            await LoadAsync();
        }
    }

    private async Task PreviousAsync()
    {
        if (page <= 1)
            return;
        page--;
        await LoadAsync();
    }

    private async Task NextAsync()
    {
        if (page >= totalPages)
            return;
        page++;
        await LoadAsync();
    }

    private string SegmentClass(bool unread) =>
        unreadOnly == unread ? "os-notifications-segment os-notifications-segment--active" : "os-notifications-segment";

    private static string RelativeTime(DateTimeOffset createdAtUtc)
    {
        var elapsed = DateTimeOffset.UtcNow - createdAtUtc;
        if (elapsed < TimeSpan.FromMinutes(1))
            return UiStrings.Common.JustNow;
        if (elapsed < TimeSpan.FromHours(1))
            return string.Format(UiStrings.Common.MinutesAgo, Math.Max(1, (int)elapsed.TotalMinutes));
        if (elapsed < TimeSpan.FromDays(1))
            return string.Format(UiStrings.Common.HoursAgo, Math.Max(1, (int)elapsed.TotalHours));
        return string.Format(UiStrings.Common.DaysAgo, Math.Max(1, (int)elapsed.TotalDays));
    }

    private void ShowActionFailure() =>
        Toasts.Notify(NotificationSeverity.Error, UiStrings.Notifications.Title, UiStrings.Notifications.ActionFailed);

    private async void OnLiveReceived(NotificationDto notification)
    {
        try
        {
            await InvokeAsync(() =>
            {
                var revision = ++liveRevision;
                pendingLive[notification.Id] = new PendingLiveNotification(notification, revision);
                if (page != 1 || (unreadOnly && notification.IsRead) || items.Any(item => item.Id == notification.Id))
                    return;

                items = items.Prepend(notification).Take(PageSize).ToArray();
                totalCount++;
                totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
                StateHasChanged();
            });
        }
        catch (Exception ex)
        {
            await DispatchExceptionAsync(ex);
        }
    }

    private async void OnCenterReconciled()
    {
        try
        {
            await InvokeAsync(LoadAsync);
        }
        catch (Exception ex)
        {
            await DispatchExceptionAsync(ex);
        }
    }

    public ValueTask DisposeAsync()
    {
        Center.LiveReceived -= OnLiveReceived;
        Center.Reconciled -= OnCenterReconciled;
        return ValueTask.CompletedTask;
    }
}
