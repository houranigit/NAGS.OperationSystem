using OperationsSystem.Blazor.Client.State;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.State;

public sealed class UserTimeZoneTests
{
    [Fact]
    public void Riyadh_wall_clock_is_converted_to_utc_without_changing_the_entered_time()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
        var local = new DateTime(2026, 8, 8, 14, 30, 0, DateTimeKind.Unspecified);

        UserTimeZone.TryToUtc(local, zone, out var utc, out var error).ShouldBeTrue();

        error.ShouldBeNull();
        utc.ShouldBe(new DateTimeOffset(2026, 8, 8, 11, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Invalid_and_ambiguous_dst_wall_clocks_are_rejected()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

        UserTimeZone.TryToUtc(
            new DateTime(2026, 3, 8, 2, 30, 0, DateTimeKind.Unspecified),
            zone,
            out _,
            out var invalidError).ShouldBeFalse();
        UserTimeZone.TryToUtc(
            new DateTime(2026, 11, 1, 1, 30, 0, DateTimeKind.Unspecified),
            zone,
            out _,
            out var ambiguousError).ShouldBeFalse();

        invalidError.ShouldNotBeNull();
        ambiguousError.ShouldNotBeNull();
        invalidError.ShouldContain("does not exist");
        ambiguousError.ShouldContain("occurs twice");
    }

    [Fact]
    public void Local_date_filter_bounds_follow_zone_rules_across_dst()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var date = new DateTime(2026, 3, 8);

        var start = UserTimeZone.DateBoundaryUtc(date, endOfDay: false, zone);
        var end = UserTimeZone.DateBoundaryUtc(date, endOfDay: true, zone);

        start.ShouldBe(new DateTimeOffset(2026, 3, 8, 6, 0, 0, TimeSpan.Zero));
        end.AddTicks(1).ShouldBe(new DateTimeOffset(2026, 3, 9, 5, 0, 0, TimeSpan.Zero));
        (end.AddTicks(1) - start).ShouldBe(TimeSpan.FromHours(23));
    }
}
