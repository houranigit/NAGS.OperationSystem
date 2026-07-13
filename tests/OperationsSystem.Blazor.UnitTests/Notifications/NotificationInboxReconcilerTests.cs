using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Features.Notifications;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Notifications;

public sealed class NotificationInboxReconcilerTests
{
    [Fact]
    public void Live_arriving_after_request_start_is_merged_without_overwriting_newer_server_rows()
    {
        var now = DateTimeOffset.UtcNow;
        var serverItem = Notification(now.AddMinutes(-1));
        var live = Notification(now);
        var pending = new Dictionary<Guid, PendingLiveNotification>
        {
            [live.Id] = new(live, Revision: 2)
        };
        var result = new PagedResult<NotificationDto>([serverItem], 1, 12, 1);

        var merged = NotificationInboxReconciler.Merge(
            result,
            pending,
            requestStartLiveRevision: 1,
            requestedPage: 1,
            requestedUnreadOnly: false,
            pageSize: 12);

        merged.Items.Select(item => item.Id).ShouldBe([live.Id, serverItem.Id]);
        merged.TotalCount.ShouldBe(2);
        merged.ConsumedPendingIds.ShouldBeEmpty();
    }

    [Fact]
    public void Response_started_after_live_event_is_authoritative_for_off_page_or_archived_records()
    {
        var live = Notification(DateTimeOffset.UtcNow.AddMinutes(-30));
        var pending = new Dictionary<Guid, PendingLiveNotification>
        {
            [live.Id] = new(live, Revision: 4)
        };
        var result = new PagedResult<NotificationDto>([], 1, 12, 0);

        var merged = NotificationInboxReconciler.Merge(
            result,
            pending,
            requestStartLiveRevision: 4,
            requestedPage: 1,
            requestedUnreadOnly: false,
            pageSize: 12);

        merged.Items.ShouldBeEmpty();
        merged.TotalCount.ShouldBe(0);
        merged.ConsumedPendingIds.ShouldBe([live.Id]);
    }

    [Fact]
    public void Subsequent_authoritative_page_prunes_off_page_pending_item_without_double_counting()
    {
        var now = DateTimeOffset.UtcNow;
        var serverItems = Enumerable.Range(0, 12)
            .Select(index => Notification(now.AddMinutes(-index)))
            .ToArray();
        var offPageLive = Notification(now.AddHours(-1));
        var pending = new Dictionary<Guid, PendingLiveNotification>
        {
            [offPageLive.Id] = new(offPageLive, Revision: 9)
        };
        var authoritative = new PagedResult<NotificationDto>(serverItems, 1, 12, 13);

        var merged = NotificationInboxReconciler.Merge(
            authoritative,
            pending,
            requestStartLiveRevision: 9,
            requestedPage: 1,
            requestedUnreadOnly: false,
            pageSize: 12);

        merged.Items.Select(item => item.Id).ShouldBe(serverItems.Select(item => item.Id));
        merged.TotalCount.ShouldBe(13);
        merged.ConsumedPendingIds.ShouldBe([offPageLive.Id]);
    }

    private static NotificationDto Notification(DateTimeOffset createdAtUtc) => new(
        Guid.NewGuid(),
        "StaffAssignedToFlight",
        "Assigned",
        "A teammate added you.",
        "تم تعيينك",
        "أضافك أحد الزملاء.",
        new Dictionary<string, string> { ["flightId"] = Guid.NewGuid().ToString() },
        IsRead: false,
        createdAtUtc,
        ReadAtUtc: null);
}
