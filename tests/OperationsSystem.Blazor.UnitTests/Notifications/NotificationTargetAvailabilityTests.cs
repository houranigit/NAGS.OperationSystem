using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Features.Notifications;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Notifications;

public sealed class NotificationTargetAvailabilityTests
{
    [Fact]
    public async Task Only_verified_targets_are_openable_and_schedule_updates_never_are()
    {
        var flight = Guid.NewGuid();
        var missing = Guid.NewGuid();
        IReadOnlyList<Guid>? requested = null;
        var targets = New((ids, _) => { requested = ids; return Task.FromResult<IReadOnlyList<Guid>>([flight, Guid.NewGuid()]); });
        var assignment = Row(flight);
        var schedule = Row(flight, "FlightScheduleUpdated");
        var missingRow = Row(missing);
        targets.IsOpenable(assignment).ShouldBeFalse();

        await targets.CheckAsync([assignment, schedule, missingRow, assignment]);

        requested!.Count.ShouldBe(2);
        targets.IsOpenable(assignment).ShouldBeTrue();
        targets.IsOpenable(schedule).ShouldBeFalse();
        targets.IsOpenable(missingRow).ShouldBeFalse();
        targets.IsOpenable(assignment with { Payload = new Dictionary<string, string> { ["flightId"] = "invalid" } }).ShouldBeFalse();
    }

    [Fact]
    public async Task Forced_open_check_revokes_a_target_that_disappeared_after_loading()
    {
        var flight = Guid.NewGuid();
        var exists = true;
        var calls = 0;
        var targets = New((_, _) => { calls++; return Task.FromResult<IReadOnlyList<Guid>>(exists ? [flight] : []); });
        var row = Row(flight);
        await targets.CheckAsync([row]);
        exists = false;
        await targets.CheckAsync([row]);
        calls.ShouldBe(1);
        await targets.CheckAsync([row], force: true);
        calls.ShouldBe(2);
        targets.IsOpenable(row).ShouldBeFalse();
    }

    [Fact]
    public async Task Unknown_availability_hides_open_without_breaking_the_inbox_and_can_retry()
    {
        var flight = Guid.NewGuid();
        var offline = false;
        var targets = New((_, _) => offline ? throw new HttpRequestException("Offline") : Task.FromResult<IReadOnlyList<Guid>>([flight]));
        var row = Row(flight);
        await targets.CheckAsync([row]);
        offline = true;
        await targets.CheckAsync([row], force: true);
        targets.IsOpenable(row).ShouldBeFalse();
        offline = false;
        await targets.CheckAsync([row]);
        targets.IsOpenable(row).ShouldBeTrue();
    }

    [Fact]
    public async Task Old_account_response_cannot_expose_targets_in_a_replacement_session()
    {
        var flight = Guid.NewGuid();
        var response = new TaskCompletionSource<IReadOnlyList<Guid>>();
        long session = 1;
        var targets = new NotificationTargetAvailability((_, _) => response.Task, () => session, () => true, TimeProvider.System);
        var row = Row(flight);
        var pending = targets.CheckAsync([row]);
        session++;
        response.SetResult([flight]);
        await pending;
        targets.IsOpenable(row).ShouldBeFalse();
    }

    [Fact]
    public async Task Older_overlapping_response_cannot_override_a_newer_missing_result()
    {
        var flight = Guid.NewGuid();
        var response = new TaskCompletionSource<IReadOnlyList<Guid>>();
        var calls = 0;
        var targets = New((_, _) => ++calls == 1 ? response.Task : Task.FromResult<IReadOnlyList<Guid>>([]));
        var row = Row(flight);
        var old = targets.CheckAsync([row]);
        await targets.CheckAsync([row], force: true);
        response.SetResult([flight]);
        await old;
        targets.IsOpenable(row).ShouldBeFalse();
    }

    [Fact]
    public async Task Large_history_is_deduplicated_and_checked_in_bounded_batches()
    {
        var sizes = new List<int>();
        var targets = New((ids, _) => { sizes.Add(ids.Count); return Task.FromResult(ids); });
        var rows = Enumerable.Range(0, 235).Select(_ => Row(Guid.NewGuid())).ToArray();
        await targets.CheckAsync(rows.Concat(rows));
        sizes.ShouldBe([100, 100, 35]);
        rows.ShouldAllBe(row => targets.IsOpenable(row));
    }

    private static NotificationTargetAvailability New(Func<IReadOnlyList<Guid>, CancellationToken, Task<IReadOnlyList<Guid>>> fetch) =>
        new(fetch, () => 1, () => true, TimeProvider.System);

    private static NotificationDto Row(Guid flightId, string kind = "StaffAssignedToFlight") => new(Guid.NewGuid(), kind,
        "Assigned", "You are assigned to a flight.", "", "", new Dictionary<string, string> { ["flightId"] = flightId.ToString() },
        false, DateTimeOffset.UtcNow, null);
}
