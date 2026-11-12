using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Features.Notifications;
using OperationsSystem.Blazor.Client.State;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Notifications;

public sealed class NotificationPresentationTests
{
    [Theory]
    [InlineData("StaffAssignedToFlight")]
    [InlineData("FlightSubmissionUpdated")]
    public async Task Presentation_stays_English_and_builds_flight_deep_link_regardless_of_app_locale(string kind)
    {
        var locale = new LocaleState(new NoOpJsRuntime());
        var flightId = Guid.NewGuid();
        var notification = Notification(flightId) with { Kind = kind };

        NotificationPresentation.Title(notification, locale).ShouldBe("Assigned");
        NotificationPresentation.Body(notification, locale).ShouldBe("A teammate added you.");

        await locale.SetLanguageAsync(LocaleState.Arabic);
        try
        {
            NotificationPresentation.Title(notification, locale).ShouldBe("Assigned");
            NotificationPresentation.Body(notification, locale).ShouldBe("A teammate added you.");
            NotificationPresentation.DeepLink(notification).ShouldBe($"/operations/flights?search={flightId}");
        }
        finally
        {
            await locale.SetLanguageAsync(LocaleState.English);
        }
    }

    [Theory]
    [InlineData(-1, "now")]
    [InlineData(0, "now")]
    [InlineData(5, "5m")]
    [InlineData(120, "2h")]
    [InlineData(4320, "3d")]
    [InlineData(10080, "1w")]
    [InlineData(40320, "4w")]
    [InlineData(43200, "1mo")]
    [InlineData(86400, "2mo")]
    public void Relative_age_uses_compact_units_and_distinguishes_months_from_minutes(int minutes, string expected)
    {
        var now = DateTimeOffset.Parse("2026-11-09T12:00:00Z");
        NotificationPresentation.RelativeAge(now.AddMinutes(-minutes), now).ShouldBe(expected);
    }

    [Theory]
    [InlineData("2026-11-09", "Today")]
    [InlineData("2026-11-08", "Yesterday")]
    [InlineData("2026-11-07", "November 7")]
    [InlineData("2025-11-07", "November 7, 2025")]
    public void Date_heading_names_recent_days_and_keeps_older_dates_in_English(string date, string expected)
    {
        NotificationPresentation.DateHeading(DateOnly.Parse(date), new(2026, 11, 9)).ShouldBe(expected);
    }

    [Fact]
    public void DeepLink_is_null_for_missing_or_invalid_flight_id()
    {
        var missing = Notification(Guid.NewGuid()) with { Payload = new Dictionary<string, string>() };
        var invalid = Notification(Guid.NewGuid()) with { Payload = new Dictionary<string, string> { ["flightId"] = "not-a-guid" } };

        NotificationPresentation.DeepLink(missing).ShouldBeNull();
        NotificationPresentation.DeepLink(invalid).ShouldBeNull();
    }

    [Fact]
    public void Schedule_updates_do_not_target_a_single_flight_even_if_legacy_payload_contains_an_id()
    {
        var scheduleUpdate = Notification(Guid.NewGuid()) with { Kind = "FlightScheduleUpdated" };

        NotificationPresentation.DeepLink(scheduleUpdate).ShouldBeNull();
    }

    [Theory]
    [InlineData("FlightReminder", "notification-tone--reminder")]
    [InlineData("StaffAssignedToFlight", "notification-tone--assignment")]
    [InlineData("FlightScheduleUpdated", "notification-tone--info")]
    [InlineData("FlightSubmissionUpdated", "notification-tone--info")]
    [InlineData("EmployeeInvitedToFlight", "notification-tone--invitation")]
    public void Notification_kinds_use_their_requested_semantic_tones(string kind, string tone)
    {
        NotificationPresentation.ToneClass(Notification(Guid.NewGuid()) with { Kind = kind }).ShouldBe(tone);
    }

    [Theory]
    [InlineData("EmployeeInvitedToFlight", NotificationCategories.Invitations, "person_add")]
    [InlineData("FlightReminder", NotificationCategories.Reminders, "alarm")]
    [InlineData("FlightScheduleUpdated", NotificationCategories.FlightUpdates, "event_repeat")]
    [InlineData("FlightSubmissionUpdated", NotificationCategories.FlightUpdates, "event_repeat")]
    [InlineData("StaffAssignedToFlight", NotificationCategories.FlightUpdates, "flight_takeoff")]
    [InlineData("LegacyNotification", NotificationCategories.FlightUpdates, "flight_takeoff")]
    public void Category_and_inbox_link_cover_known_and_unclassified_notifications(string kind, string category, string icon)
    {
        var notification = Notification(Guid.NewGuid()) with { Kind = kind };

        NotificationPresentation.Category(notification).ShouldBe(category);
        NotificationPresentation.Icon(notification).ShouldBe(icon);
        NotificationPresentation.InboxLink(notification).ShouldBe("/notifications");
    }

    private static NotificationDto Notification(Guid flightId) => new(
        Guid.NewGuid(),
        "StaffAssignedToFlight",
        "Assigned",
        "A teammate added you.",
        "تم تعيينك",
        "أضافك أحد الزملاء.",
        new Dictionary<string, string> { ["FLIGHTID"] = flightId.ToString() },
        false,
        DateTimeOffset.UtcNow,
        null);

    private sealed class NoOpJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);
    }
}
