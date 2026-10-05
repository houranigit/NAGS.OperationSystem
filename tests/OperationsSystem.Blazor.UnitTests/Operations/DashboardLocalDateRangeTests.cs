using System.Text.Json;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Features.Operations;
using OperationsSystem.Blazor.Client.Features.Operations.Components;
using OperationsSystem.Blazor.Client.State;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Operations;

public sealed class DashboardLocalDateRangeTests
{
    [Fact]
    public async Task Initial_today_uses_the_browser_calendar_date_when_utc_has_already_changed_day()
    {
        var zone = await ChicagoAsync();
        var now = new DateTimeOffset(2026, 10, 5, 1, 0, 0, TimeSpan.Zero);

        var range = DashboardLocalDateRange.ForPreset(zone, DashboardPeriodPreset.Today, now)!;

        range.FromDate.ShouldBe(new DateTime(2026, 10, 4));
        range.ToDate.ShouldBe(range.FromDate);
        range.FromUtc.ShouldBe(new DateTimeOffset(2026, 10, 4, 5, 0, 0, TimeSpan.Zero));
        range.ToUtc.ShouldBe(new DateTimeOffset(2026, 10, 5, 5, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(3, 8, 6, 5, 23)]
    [InlineData(11, 1, 5, 6, 25)]
    public async Task Local_day_filters_include_exactly_the_dst_day_with_an_exclusive_end(
        int month, int day, int startHour, int endHour, int hours)
    {
        var zone = await ChicagoAsync();
        var date = new DateTime(2026, month, day);

        var range = DashboardLocalDateRange.ForDates(zone, date, date);

        range.FromUtc.ShouldBe(new DateTimeOffset(2026, month, day, startHour, 0, 0, TimeSpan.Zero));
        range.ToUtc.ShouldBe(new DateTimeOffset(date.AddDays(1).AddHours(endHour), TimeSpan.Zero));
        (range.ToUtc - range.FromUtc).ShouldBe(TimeSpan.FromHours(hours));
        zone.ToLocal(range.FromUtc!.Value).Date.ShouldBe(date);
        zone.ToLocal(range.ToUtc!.Value.AddTicks(-1)).Date.ShouldBe(date);
        zone.ToLocal(range.ToUtc.Value).Date.ShouldBe(date.AddDays(1));
    }

    [Fact]
    public async Task Last_month_uses_local_calendar_boundaries_with_different_dst_offsets()
    {
        var zone = await ChicagoAsync();
        var now = new DateTimeOffset(2026, 4, 1, 8, 0, 0, TimeSpan.Zero);

        var range = DashboardLocalDateRange.ForPreset(zone, DashboardPeriodPreset.LastMonth, now)!;

        range.FromDate.ShouldBe(new DateTime(2026, 3, 1));
        range.ToDate.ShouldBe(new DateTime(2026, 3, 31));
        range.FromUtc.ShouldBe(new DateTimeOffset(2026, 3, 1, 6, 0, 0, TimeSpan.Zero));
        range.ToUtc.ShouldBe(new DateTimeOffset(2026, 4, 1, 5, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Three_month_preset_includes_the_entire_current_local_day()
    {
        var zone = await ChicagoAsync();
        var now = new DateTimeOffset(2026, 10, 5, 1, 0, 0, TimeSpan.Zero);

        var range = DashboardLocalDateRange.ForPreset(zone, DashboardPeriodPreset.LastThreeMonths, now)!;

        range.FromDate.ShouldBe(new DateTime(2026, 7, 4));
        range.ToDate.ShouldBe(new DateTime(2026, 10, 4));
        range.FromUtc.ShouldBe(new DateTimeOffset(2026, 7, 4, 5, 0, 0, TimeSpan.Zero));
        range.ToUtc.ShouldBe(new DateTimeOffset(2026, 10, 5, 5, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task A_custom_range_keeps_the_selected_end_day_and_max_keeps_unbounded_history()
    {
        var zone = await ChicagoAsync();

        var custom = DashboardLocalDateRange.ForDates(zone, new DateTime(2026, 3, 7), new DateTime(2026, 3, 9));
        var max = DashboardLocalDateRange.ForPreset(zone, DashboardPeriodPreset.Max, DateTimeOffset.UtcNow)!;

        custom.FromUtc.ShouldBe(new DateTimeOffset(2026, 3, 7, 6, 0, 0, TimeSpan.Zero));
        custom.ToUtc.ShouldBe(new DateTimeOffset(2026, 3, 10, 5, 0, 0, TimeSpan.Zero));
        max.FromUtc.ShouldBeNull();
        max.ToUtc.ShouldBeNull();
        max.FromDate.ShouldBeNull();
        max.ToDate.ShouldBeNull();
    }

    [Theory]
    [InlineData(3, 8, 6, 5, 23)]
    [InlineData(11, 1, 5, 6, 25)]
    public async Task Rollover_waits_for_the_next_local_midnight_across_dst(
        int month, int day, int currentHour, int nextHour, int hours)
    {
        var zone = await ChicagoAsync();
        var now = new DateTimeOffset(2026, month, day, currentHour, 0, 0, TimeSpan.Zero);

        var rollover = DashboardLocalDateRange.NextMidnightUtc(zone, now);

        rollover.ShouldBe(new DateTimeOffset(new DateTime(2026, month, day).AddDays(1).AddHours(nextHour), TimeSpan.Zero));
        (rollover - now).ShouldBe(TimeSpan.FromHours(hours));
    }

    internal static async Task<UserTimeZone> ChicagoAsync()
    {
        var zone = new UserTimeZone(new BrowserZoneRuntime());
        await zone.InitializeAsync();
        zone.Id.ShouldBe("America/Chicago");
        return zone;
    }

    private sealed class BrowserZoneRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            identifier.ShouldBe("operationsSystem.timeZone.get");
            return ValueTask.FromResult(JsonSerializer.Deserialize<TValue>("{\"Id\":\"America/Chicago\",\"OffsetMinutes\":-300}")!);
        }
    }
}
