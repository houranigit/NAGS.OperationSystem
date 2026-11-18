using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Abstractions;
using Notifications.Application.Features;
using Notifications.Application.IntegrationEvents;
using Notifications.Contracts;
using Notifications.Domain.Notifications;
using Notifications.Domain.Preferences;
using Notifications.Infrastructure.Persistence;
using Operations.Contracts;
using Shouldly;

namespace Notifications.Application.UnitTests;

public sealed class NotificationPreferencesTests
{
    [Fact]
    public async Task Unconfigured_accounts_start_enabled_without_writing_settings()
    {
        await using var db = NewDb();
        var handler = new GetMyNotificationPreferencesQueryHandler(db, new UserContext(Guid.NewGuid()));

        var result = await handler.Handle(new(), CancellationToken.None);

        result.Value.ShouldBe(new NotificationPreferencesDto());
        (await db.NotificationPreferences.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Updates_affect_only_the_authenticated_account_and_selected_choice()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var anotherUserId = Guid.NewGuid();
        var update = new UpdateMyNotificationPreferenceCommandHandler(db, new UserContext(userId));

        await update.Handle(new(NotificationPreferenceKeys.DirectShares, false), CancellationToken.None);
        await update.Handle(new(NotificationPreferenceKeys.ScheduleChanges, false), CancellationToken.None);
        var result = await update.Handle(new(NotificationPreferenceKeys.DirectShares, true), CancellationToken.None);
        var other = await new GetMyNotificationPreferencesQueryHandler(db, new UserContext(anotherUserId)).Handle(new(), CancellationToken.None);

        result.Value.ShouldBe(new NotificationPreferencesDto(ScheduleChanges: false));
        other.Value.ShouldBe(new NotificationPreferencesDto());
        (await db.NotificationPreferences.CountAsync()).ShouldBe(2);
        db.NotificationPreferences.ShouldAllBe(preference => preference.UserId == userId);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("FlightAssignments", false)]
    [InlineData("unknown", true)]
    [InlineData("flightAssignments", null)]
    public async Task Invalid_requests_do_not_write_preferences(string? key, bool? enabled)
    {
        await using var db = NewDb();
        var result = await new UpdateMyNotificationPreferenceCommandHandler(db, new UserContext(Guid.NewGuid()))
            .Handle(new(key, enabled), CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.Validation);
        (await db.NotificationPreferences.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Reading_and_writing_require_an_authenticated_user()
    {
        await using var db = NewDb();
        var user = new UserContext(null);
        var read = await new GetMyNotificationPreferencesQueryHandler(db, user).Handle(new(), CancellationToken.None);
        var write = await new UpdateMyNotificationPreferenceCommandHandler(db, user).Handle(new("enabled", false), CancellationToken.None);

        read.Error.Type.ShouldBe(ErrorType.Unauthorized);
        write.Error.Type.ShouldBe(ErrorType.Unauthorized);
        (await db.NotificationPreferences.CountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData(NotificationKind.StaffAssignedToFlight, NotificationPreferenceKeys.FlightAssignments)]
    [InlineData(NotificationKind.EmployeeInvitedToFlight, NotificationPreferenceKeys.FlightInvitations)]
    [InlineData(NotificationKind.FlightScheduleUpdated, NotificationPreferenceKeys.ScheduleChanges)]
    [InlineData(NotificationKind.FlightSubmissionUpdated, NotificationPreferenceKeys.ColleagueEdits)]
    [InlineData(NotificationKind.FlightReminder, NotificationPreferenceKeys.FlightReminders)]
    [InlineData("FlightSharedWithEmployee", NotificationPreferenceKeys.DirectShares)]
    [InlineData("FlightSharedWithStation", NotificationPreferenceKeys.StationShares)]
    [InlineData("FlightSyncIssue", NotificationPreferenceKeys.SyncIssues)]
    public async Task Each_kind_obeys_its_own_account_preference(string kind, string key)
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        db.NotificationPreferences.Add(NotificationPreference.Create(userId, key, false).Value);
        await db.SaveChangesAsync();

        (await NotificationPreferenceFilter.IsEnabledAsync(db, userId, kind)).ShouldBeFalse();
        (await NotificationPreferenceFilter.IsEnabledAsync(db, Guid.NewGuid(), kind)).ShouldBeTrue();
    }

    [Fact]
    public async Task Master_switch_preserves_individual_choices_and_blocks_unrecognized_future_kinds()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var update = new UpdateMyNotificationPreferenceCommandHandler(db, new UserContext(userId));
        await update.Handle(new("flightReminders", false), CancellationToken.None);
        await update.Handle(new("enabled", false), CancellationToken.None);

        (await NotificationPreferenceFilter.IsEnabledAsync(db, userId, "FutureKind")).ShouldBeFalse();
        var restored = await update.Handle(new("enabled", true), CancellationToken.None);

        restored.Value.ShouldBe(new NotificationPreferencesDto(FlightReminders: false));
        (await NotificationPreferenceFilter.IsEnabledAsync(db, userId, NotificationKind.StaffAssignedToFlight)).ShouldBeTrue();
        (await NotificationPreferenceFilter.IsEnabledAsync(db, userId, NotificationKind.FlightReminder)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(FlightAssignmentSource.Roster, "flightAssignments")]
    [InlineData(FlightAssignmentSource.Invite, "flightInvitations")]
    public async Task Disabled_assignments_are_terminally_consumed_without_inbox_or_push(FlightAssignmentSource source, string key)
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        db.NotificationPreferences.Add(NotificationPreference.Create(userId, key, false).Value);
        await db.SaveChangesAsync();
        var pusher = new CapturingPusher();
        var handler = new FlightEmployeeAssignedHandler(db, new StaffReader(staffId, userId), pusher, TimeProvider.System);
        var notificationEvent = new FlightEmployeeAssigned
        {
            FlightId = Guid.NewGuid(), FlightNumber = "SV204", StaffMemberId = staffId,
            AssignedByUserId = Guid.NewGuid(), AssignedByDisplayName = "A teammate", Source = source
        };

        await handler.HandleAsync(notificationEvent);
        await new UpdateMyNotificationPreferenceCommandHandler(db, new UserContext(userId)).Handle(new(key, true), CancellationToken.None);
        await handler.HandleAsync(notificationEvent);

        (await db.Notifications.CountAsync()).ShouldBe(0);
        (await db.InboxMessages.CountAsync()).ShouldBe(1);
        pusher.Count.ShouldBe(0);
    }

    private static NotificationsDbContext NewDb() => new(new DbContextOptionsBuilder<NotificationsDbContext>()
        .UseInMemoryDatabase($"notification-preferences-{Guid.NewGuid()}").Options);

    private sealed class UserContext(Guid? userId) : IUserContext
    {
        public bool IsAuthenticated => userId.HasValue;
        public Guid? UserId => userId;
        public UserType? UserType => null;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => false;
    }

    private sealed class StaffReader(Guid staffId, Guid userId) : IStaffNotificationReader
    {
        public Task<StaffNotificationRecipient?> GetStaffRecipientAsync(Guid staffMemberId, CancellationToken cancellationToken = default) =>
            Task.FromResult<StaffNotificationRecipient?>(new(staffId, "Recipient", userId));
    }

    private sealed class CapturingPusher : INotificationPusher
    {
        public int Count { get; private set; }
        public Task PushAsync(Guid recipientUserId, NotificationDto notification, CancellationToken cancellationToken = default)
        {
            Count++;
            return Task.CompletedTask;
        }
    }
}
