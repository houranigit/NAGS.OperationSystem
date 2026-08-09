using Operations.Application.Features.Flights;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class FlightScheduleTimeZoneTests
{
    [Fact]
    public void Recurring_local_time_is_resolved_with_each_dates_zone_rules()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

        ScheduleFlightsCommandHandler.TryCombineUtc(
            new DateOnly(2026, 1, 15),
            new TimeOnly(10, 0),
            zone,
            out var winter,
            out _).ShouldBeTrue();
        ScheduleFlightsCommandHandler.TryCombineUtc(
            new DateOnly(2026, 7, 15),
            new TimeOnly(10, 0),
            zone,
            out var summer,
            out _).ShouldBeTrue();

        winter.ShouldBe(new DateTimeOffset(2026, 1, 15, 16, 0, 0, TimeSpan.Zero));
        summer.ShouldBe(new DateTimeOffset(2026, 7, 15, 15, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Riyadh_recurring_time_is_converted_to_the_correct_utc_instant()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");

        ScheduleFlightsCommandHandler.TryCombineUtc(
            new DateOnly(2026, 8, 8),
            new TimeOnly(10, 0),
            zone,
            out var instant,
            out var error).ShouldBeTrue();

        error.ShouldBeEmpty();
        instant.ShouldBe(new DateTimeOffset(2026, 8, 8, 7, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(2026, 3, 8, 2, 30, "does not exist")]
    [InlineData(2026, 11, 1, 1, 30, "occurs twice")]
    public void Dst_gap_and_overlap_require_an_unambiguous_time(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        string expectedError)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

        ScheduleFlightsCommandHandler.TryCombineUtc(
            new DateOnly(year, month, day),
            new TimeOnly(hour, minute),
            zone,
            out _,
            out var error).ShouldBeFalse();

        error.ShouldContain(expectedError);
    }

    [Fact]
    public void Browser_fixed_offset_fallback_remains_compatible_with_bulk_scheduling()
    {
        ScheduleFlightsCommandHandler.TryResolveTimeZone(
            "Browser UTC+03:00",
            out var zone).ShouldBeTrue();

        zone.BaseUtcOffset.ShouldBe(TimeSpan.FromHours(3));
    }
}
