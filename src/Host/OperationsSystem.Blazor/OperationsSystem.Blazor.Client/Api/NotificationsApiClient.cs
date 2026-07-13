using System.Globalization;

namespace OperationsSystem.Blazor.Client.Api;

/// <summary>Typed access to the authenticated user's notification inbox.</summary>
public sealed class NotificationsApiClient(BrowserApiClient api)
{
    public Task<PagedResult<NotificationDto>> GetInboxAsync(
        int page = 1,
        int pageSize = 20,
        bool unreadOnly = false,
        CancellationToken ct = default)
    {
        var query = string.Join('&',
            $"page={page.ToString(CultureInfo.InvariantCulture)}",
            $"pageSize={pageSize.ToString(CultureInfo.InvariantCulture)}",
            $"unreadOnly={unreadOnly.ToString().ToLowerInvariant()}");

        return api.GetAsync<PagedResult<NotificationDto>>($"/notifications/me?{query}", ct);
    }

    public Task<UnreadNotificationCount> GetUnreadCountAsync(CancellationToken ct = default) =>
        api.GetAsync<UnreadNotificationCount>("/notifications/me/unread-count", ct);

    public Task MarkAsReadAsync(Guid id, CancellationToken ct = default) =>
        api.PostAsync($"/notifications/{id}/read", ct);

    public Task MarkAllAsReadAsync(CancellationToken ct = default) =>
        api.PostAsync("/notifications/me/mark-all-read", ct);

    public Task ArchiveAsync(Guid id, CancellationToken ct = default) =>
        api.PostAsync($"/notifications/{id}/archive", ct);

    public Task ArchiveAllAsync(CancellationToken ct = default) =>
        api.PostAsync("/notifications/me/archive-all", ct);
}
