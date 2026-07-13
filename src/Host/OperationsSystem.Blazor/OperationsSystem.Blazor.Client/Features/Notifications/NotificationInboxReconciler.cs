using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Features.Notifications;

internal sealed record PendingLiveNotification(NotificationDto Notification, long Revision);

internal sealed record NotificationInboxMergeResult(
    IReadOnlyList<NotificationDto> Items,
    long TotalCount,
    IReadOnlyList<Guid> ConsumedPendingIds);

/// <summary>
/// Merges only hub deliveries that arrived after an inbox request began. A delivery observed before
/// the request is guaranteed to have been persisted already, so that response is authoritative even
/// when the record is archived, filtered out, or beyond the requested page.
/// </summary>
internal static class NotificationInboxReconciler
{
    public static NotificationInboxMergeResult Merge(
        PagedResult<NotificationDto> result,
        IReadOnlyDictionary<Guid, PendingLiveNotification> pending,
        long requestStartLiveRevision,
        int requestedPage,
        bool requestedUnreadOnly,
        int pageSize)
    {
        var resultIds = result.Items.Select(item => item.Id).ToHashSet();
        var consumedIds = pending
            .Where(item => item.Value.Revision <= requestStartLiveRevision || resultIds.Contains(item.Key))
            .Select(item => item.Key)
            .ToArray();

        var liveDuringRequest = requestedPage == 1
            ? pending.Values
                .Where(item => item.Revision > requestStartLiveRevision)
                .Where(item => !requestedUnreadOnly || !item.Notification.IsRead)
                .Where(item => !resultIds.Contains(item.Notification.Id))
                .Select(item => item.Notification)
                .ToArray()
            : [];

        var items = liveDuringRequest
            .Concat(result.Items)
            .DistinctBy(item => item.Id)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .Take(pageSize)
            .ToArray();

        return new NotificationInboxMergeResult(
            items,
            result.TotalCount + liveDuringRequest.Length,
            consumedIds);
    }
}
