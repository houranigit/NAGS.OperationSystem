using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Abstractions;
using Notifications.Application.IntegrationEvents;
using Notifications.Contracts;
using Notifications.Domain.Notifications;
using Notifications.Infrastructure.Persistence;
using Operations.Contracts;
using Shouldly;

namespace Notifications.Application.UnitTests;

public sealed class FlightSubmissionUpdatedHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(FlightSubmissionKind.Arrival, "arrival", "arrival submission", "بيانات الوصول التي أرسلتها")]
    [InlineData(FlightSubmissionKind.Departure, "departure", "departure submission", "بيانات المغادرة التي أرسلتها")]
    [InlineData(FlightSubmissionKind.Service, "service", "service entry", "سجل الخدمة الذي أنشأته")]
    [InlineData(FlightSubmissionKind.Task, "task", "task entry", "سجل المهمة الذي أنشأته")]
    public async Task Correction_notifies_original_account_once_with_localized_content_and_only_navigation_payload(
        FlightSubmissionKind kind, string kindKey, string entryEn, string entryAr)
    {
        await using var db = NewDb();
        var pusher = new CapturingPusher();
        var handler = new FlightSubmissionUpdatedHandler(db, pusher, new Clock());
        var integrationEvent = Event(kind);

        await handler.HandleAsync(integrationEvent);
        await handler.HandleAsync(integrationEvent);

        var notification = await db.Notifications.SingleAsync();
        notification.Id.ShouldBe(integrationEvent.EventId);
        notification.RecipientUserId.ShouldBe(integrationEvent.SubmittedByUserId);
        notification.RecipientUserId.ShouldNotBe(integrationEvent.UpdatedByUserId);
        notification.Kind.ShouldBe(NotificationKind.FlightSubmissionUpdated);
        notification.TitleEn.ShouldBe($"Your {entryEn} was updated");
        notification.BodyEn.ShouldBe($"Hamza Hourani updated your {entryEn} for flight SV100.");
        notification.TitleAr.ShouldBe($"تم تحديث {entryAr}");
        notification.BodyAr.ShouldContain("Hamza Hourani");
        notification.BodyAr.ShouldContain(entryAr);
        notification.BodyAr.ShouldContain("SV100");
        var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(notification.PayloadJson)!;
        payload.Count.ShouldBe(4);
        payload["flightId"].ShouldBe(integrationEvent.FlightId.ToString());
        payload["flightNumber"].ShouldBe("SV100");
        payload["submissionId"].ShouldBe(integrationEvent.SubmissionId.ToString());
        payload["submissionKind"].ShouldBe(kindKey);
        payload.Keys.ShouldNotContain(key => key.Contains("signature", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("storage", StringComparison.OrdinalIgnoreCase) || key.Contains("remarks", StringComparison.OrdinalIgnoreCase));
        notification.DeliveredAtUtc.ShouldBe(Now);
        (await db.InboxMessages.CountAsync()).ShouldBe(1);
        var pushed = pusher.Items.ShouldHaveSingleItem();
        pushed.Recipient.ShouldBe(integrationEvent.SubmittedByUserId);
        pushed.Notification.Id.ShouldBe(integrationEvent.EventId);
    }

    [Theory]
    [InlineData(FlightSubmissionKind.Departure, FlightSubmissionAction.Updated)]
    [InlineData(FlightSubmissionKind.Service, FlightSubmissionAction.Updated)]
    [InlineData(FlightSubmissionKind.Task, FlightSubmissionAction.Updated)]
    [InlineData(FlightSubmissionKind.Service, FlightSubmissionAction.Deleted)]
    [InlineData(FlightSubmissionKind.Task, FlightSubmissionAction.Deleted)]
    public async Task Persisted_notification_retries_transport_with_the_same_identity_after_failure(FlightSubmissionKind kind, FlightSubmissionAction action)
    {
        await using var db = NewDb();
        var pusher = new FlakyPusher();
        var handler = new FlightSubmissionUpdatedHandler(db, pusher, new Clock());
        var integrationEvent = Event(kind) with { Action = action };

        await Should.ThrowAsync<InvalidOperationException>(() => handler.HandleAsync(integrationEvent));
        (await db.Notifications.SingleAsync()).DeliveredAtUtc.ShouldBeNull();
        db.ChangeTracker.Clear();

        await handler.HandleAsync(integrationEvent);
        await handler.HandleAsync(integrationEvent);

        pusher.AttemptedIds.ShouldBe(new[] { integrationEvent.EventId, integrationEvent.EventId });
        (await db.Notifications.SingleAsync()).DeliveredAtUtc.ShouldBe(Now);
        (await db.InboxMessages.CountAsync()).ShouldBe(1);
    }

    [Theory]
    [InlineData(FlightSubmissionKind.Arrival, false)]
    [InlineData(FlightSubmissionKind.Arrival, true)]
    [InlineData(FlightSubmissionKind.Service, false)]
    [InlineData(FlightSubmissionKind.Service, true)]
    [InlineData(FlightSubmissionKind.Task, false)]
    [InlineData(FlightSubmissionKind.Task, true)]
    public async Task Missing_original_account_or_self_correction_is_terminally_skipped(FlightSubmissionKind kind, bool self)
    {
        await using var db = NewDb();
        var pusher = new CapturingPusher();
        var handler = new FlightSubmissionUpdatedHandler(db, pusher, new Clock());
        var integrationEvent = Event(kind);
        integrationEvent = integrationEvent with
        {
            SubmittedByUserId = self ? integrationEvent.UpdatedByUserId : Guid.Empty
        };

        await handler.HandleAsync(integrationEvent);
        await handler.HandleAsync(integrationEvent);

        (await db.Notifications.CountAsync()).ShouldBe(0);
        (await db.InboxMessages.CountAsync()).ShouldBe(1);
        pusher.Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(FlightSubmissionKind.Service, "service", "الخدمة")]
    [InlineData(FlightSubmissionKind.Task, "task", "المهمة")]
    public async Task Foreign_deletion_notifies_creator_once_with_explicit_action_and_private_data_free_payload(
        FlightSubmissionKind kind, string name, string arabic)
    {
        await using var db = NewDb(); var pusher = new CapturingPusher();
        var handler = new FlightSubmissionUpdatedHandler(db, pusher, new Clock());
        var integrationEvent = Event(kind) with { Action = FlightSubmissionAction.Deleted, SubmittedByStaffMemberId = null };

        await handler.HandleAsync(integrationEvent); await handler.HandleAsync(integrationEvent);

        var item = await db.Notifications.SingleAsync();
        item.RecipientUserId.ShouldBe(integrationEvent.SubmittedByUserId); item.TitleEn.ShouldBe($"Your {name} entry was deleted");
        item.BodyEn.ShouldBe($"Hamza Hourani deleted your {name} entry for flight SV100.");
        item.TitleAr.ShouldContain("حذف"); item.TitleAr.ShouldContain(arabic);
        var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(item.PayloadJson)!;
        payload.Count.ShouldBe(5); payload["action"].ShouldBe("deleted"); payload["submissionKind"].ShouldBe(name);
        payload.Keys.ShouldNotContain(key => key.Contains("signature") || key.Contains("storage") || key.Contains("remarks"));
        pusher.Items.ShouldHaveSingleItem(); (await db.InboxMessages.CountAsync()).ShouldBe(1);
    }

    private static FlightSubmissionUpdated Event(FlightSubmissionKind kind) => new()
    {
        FlightId = Guid.NewGuid(), FlightNumber = "SV100", SubmissionId = Guid.NewGuid(), SubmissionKind = kind,
        SubmittedByStaffMemberId = Guid.NewGuid(), SubmittedByUserId = Guid.NewGuid(),
        UpdatedByStaffMemberId = Guid.NewGuid(), UpdatedByUserId = Guid.NewGuid(),
        UpdatedByDisplayName = "Hamza Hourani", OccurredOnUtc = Now
    };

    private static NotificationsDbContext NewDb() => new(new DbContextOptionsBuilder<NotificationsDbContext>()
        .UseInMemoryDatabase($"notifications-{Guid.NewGuid()}").Options);

    private sealed class CapturingPusher : INotificationPusher
    {
        public List<(Guid Recipient, NotificationDto Notification)> Items { get; } = [];

        public Task PushAsync(Guid recipientUserId, NotificationDto notification, CancellationToken cancellationToken = default)
        {
            Items.Add((recipientUserId, notification));
            return Task.CompletedTask;
        }
    }

    private sealed class FlakyPusher : INotificationPusher
    {
        public List<Guid> AttemptedIds { get; } = [];

        public Task PushAsync(Guid recipientUserId, NotificationDto notification, CancellationToken cancellationToken = default)
        {
            AttemptedIds.Add(notification.Id);
            return AttemptedIds.Count == 1
                ? Task.FromException(new InvalidOperationException("Transient transport failure."))
                : Task.CompletedTask;
        }
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
}
