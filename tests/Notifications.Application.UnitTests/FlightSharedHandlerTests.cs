using System.Text.Json;
using MasterData.Contracts.Readers;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Abstractions;
using Notifications.Application.IntegrationEvents;
using Notifications.Contracts;
using Notifications.Domain.Notifications;
using Notifications.Domain.Preferences;
using Notifications.Infrastructure.Persistence;
using Operations.Contracts;
using Shouldly;

namespace Notifications.Application.UnitTests;

public sealed class FlightSharedHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false, NotificationKind.FlightSharedWithEmployee)]
    [InlineData(true, NotificationKind.FlightSharedWithStation)]
    public async Task Share_delivery_persists_bilingual_information_once_and_preserves_target_identity(bool stationWide, string kind)
    {
        await using var db = NewDb();
        var message = Message(stationWide);
        var recipient = Guid.NewGuid(); var push = new Pusher();
        var handler = new FlightSharedHandler(db, new StaffReader(recipient), push, new Clock());

        await handler.HandleAsync(message);
        await handler.HandleAsync(message);

        var saved = await db.Notifications.SingleAsync();
        saved.Id.ShouldBe(message.EventId); saved.RecipientUserId.ShouldBe(recipient); saved.Kind.ShouldBe(kind);
        saved.DeliveredAtUtc.ShouldBe(Now); saved.TitleAr.ShouldNotBeNullOrWhiteSpace();
        saved.BodyEn.ShouldContain("Author"); saved.BodyEn.ShouldContain(message.FlightNumber);
        using var payload = JsonDocument.Parse(saved.PayloadJson);
        payload.RootElement.GetProperty("shareId").GetString().ShouldBe(message.ShareId.ToString());
        payload.RootElement.GetProperty("flightId").GetString().ShouldBe(message.FlightId.ToString());
        payload.RootElement.GetProperty("stationId").GetString().ShouldBe(message.StationId.ToString());
        push.Delivered.ShouldHaveSingleItem().Id.ShouldBe(saved.Id);
        (await db.InboxMessages.CountAsync()).ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unlinked_and_self_recipients_are_terminal_without_inbox_alerts(bool self)
    {
        await using var db = NewDb(); var message = Message(); var push = new Pusher();
        var reader = new StaffReader(self ? message.SharedByUserId : null);
        var handler = new FlightSharedHandler(db, reader, push, new Clock());
        await handler.HandleAsync(message);
        reader.LinkedUserId = Guid.NewGuid();
        await handler.HandleAsync(message);

        (await db.Notifications.CountAsync()).ShouldBe(0); push.Delivered.ShouldBeEmpty();
        (await db.InboxMessages.CountAsync()).ShouldBe(1);
    }

    [Theory]
    [InlineData(false, NotificationPreferenceKeys.DirectShares)]
    [InlineData(true, NotificationPreferenceKeys.StationShares)]
    [InlineData(false, NotificationPreferenceKeys.Enabled)]
    public async Task Disabled_share_choices_suppress_only_their_account_and_are_not_replayed_when_enabled(bool stationWide, string key)
    {
        await using var db = NewDb(); var recipient = Guid.NewGuid(); var message = Message(stationWide);
        var preference = NotificationPreference.Create(recipient, key, false).Value;
        db.NotificationPreferences.Add(preference); await db.SaveChangesAsync();
        var push = new Pusher(); var handler = new FlightSharedHandler(db, new StaffReader(recipient), push, new Clock());

        await handler.HandleAsync(message);
        preference.SetEnabled(true); await db.SaveChangesAsync();
        await handler.HandleAsync(message);
        (await db.Notifications.CountAsync()).ShouldBe(0); push.Delivered.ShouldBeEmpty();

        preference.SetEnabled(false); await db.SaveChangesAsync();
        var another = new FlightSharedHandler(db, new StaffReader(Guid.NewGuid()), push, new Clock());
        await another.HandleAsync(Message(stationWide));
        push.Delivered.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Transport_retry_keeps_the_original_account_and_id_and_rechecks_preferences()
    {
        await using var db = NewDb(); var recipient = Guid.NewGuid(); var message = Message();
        var reader = new StaffReader(recipient); var push = new Pusher { Fail = true };
        var handler = new FlightSharedHandler(db, reader, push, new Clock());
        await Should.ThrowAsync<IOException>(() => handler.HandleAsync(message));
        (await db.Notifications.SingleAsync()).DeliveredAtUtc.ShouldBeNull();
        reader.LinkedUserId = Guid.NewGuid();
        var preference = NotificationPreference.Create(recipient, NotificationPreferenceKeys.DirectShares, false).Value;
        db.NotificationPreferences.Add(preference); await db.SaveChangesAsync();
        push.Fail = false;
        await handler.HandleAsync(message);
        push.Delivered.ShouldBeEmpty();

        preference.SetEnabled(true); await db.SaveChangesAsync();
        await handler.HandleAsync(message);
        await handler.HandleAsync(message);
        push.Delivered.ShouldHaveSingleItem().Id.ShouldBe(message.EventId);
        push.Recipient.ShouldBe(recipient);
        (await db.Notifications.CountAsync()).ShouldBe(1);
    }

    private static FlightShared Message(bool stationWide = false) => new()
    {
        ShareId = Guid.NewGuid(), FlightId = Guid.NewGuid(), FlightNumber = "SV123", StationId = Guid.NewGuid(),
        StaffMemberId = Guid.NewGuid(), SharedByUserId = Guid.NewGuid(), SharedByDisplayName = "Author",
        IsStationWide = stationWide, OccurredOnUtc = Now
    };

    private static NotificationsDbContext NewDb() => new(new DbContextOptionsBuilder<NotificationsDbContext>()
        .UseInMemoryDatabase($"flight-share-notifications-{Guid.NewGuid()}").Options);

    private sealed class StaffReader(Guid? linkedUserId) : IStaffNotificationReader
    {
        public Guid? LinkedUserId { get; set; } = linkedUserId;
        public Task<StaffNotificationRecipient?> GetStaffRecipientAsync(Guid staffMemberId, CancellationToken cancellationToken) =>
            Task.FromResult<StaffNotificationRecipient?>(new(staffMemberId, "Recipient", LinkedUserId));
    }

    private sealed class Pusher : INotificationPusher
    {
        public bool Fail { get; set; }
        public Guid? Recipient { get; private set; }
        public List<NotificationDto> Delivered { get; } = [];
        public Task PushAsync(Guid recipientUserId, NotificationDto notification, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new IOException("Transport unavailable.");
            Recipient = recipientUserId; Delivered.Add(notification); return Task.CompletedTask;
        }
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
}
