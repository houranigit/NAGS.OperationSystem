using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MasterData.Contracts.Seeding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Operations.Application.Contracts;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.BackgroundJobs;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed partial class FlightVisibilityScopeTests
{
    [Fact]
    public async Task Auto_close_numbers_only_untouched_overdue_per_landing_flights_and_hides_them_from_mobile()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var staff = await CreateStaffLoginAsync(admin, refs, RegularStaffPermissions);
        using var client = staff.Client;
        var user = await client.GetFromJsonAsync<JsonElement>($"{IdentityBase}/me");
        var userId = user.GetProperty("id").GetGuid();
        var employee = new StaffMemberSnapshot(staff.StaffId, "Flight operator", "AUTO-CLOSE");
        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddHours(-1);
        var backlog = MakeFlight("8791", now.AddDays(-30));
        var due = MakeFlight("8792", cutoff);
        var tooSoon = MakeFlight("8793", cutoff.AddSeconds(1));
        var regular = MakeFlight("8794", cutoff, perLanding: false);
        var withArrival = MakeFlight("8795", cutoff);
        var withService = MakeFlight("8796", cutoff);
        var withTask = MakeFlight("8797", cutoff);
        var canceled = MakeFlight("8798", cutoff);
        var manuallyApproved = MakeFlight("8799", cutoff);
        var work = TimeWindow.Create(now.AddMinutes(-10), null).Value;
        withArrival.SubmitArrival(employee, ArrivalDetails.Create(null, now.AddMinutes(-15),
            new(Guid.NewGuid(), "Airbus", "A320"), "HZ-TEST", employee, null).Value, userId, now).IsSuccess.ShouldBeTrue();
        withService.AddService(new(refs.ServiceId, "Service"), null, work, null, userId, now).IsSuccess.ShouldBeTrue();
        withTask.AddTask(FlightTaskType.Minor, null, null, work, null, userId, now).IsSuccess.ShouldBeTrue();
        var cancellation = CancellationDetails.Create(now.AddMinutes(-10), null).Value;
        canceled.SubmitCancellation(employee, cancellation, userId, now).IsSuccess.ShouldBeTrue();
        manuallyApproved.SubmitCancellation(employee, cancellation, userId, now).IsSuccess.ShouldBeTrue();
        manuallyApproved.Approve(userId, "Flight reviewer", now, 1).IsSuccess.ShouldBeTrue();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            db.Flights.AddRange(backlog, due, tooSoon, regular, withArrival, withService, withTask, canceled, manuallyApproved);
            db.FlightUserActivities.Add(FlightUserActivity.Create(userId, backlog.Id, now, usedSearch: false));
            await db.SaveChangesAsync();
        }

        var mobilePath = $"{Base}/flights/mobile";
        var hiddenIds = new[] { backlog.Id, due.Id };
        var mobileBefore = (await client.GetFromJsonAsync<MobileFlightPageDto>($"{mobilePath}?category=PerLanding"))!;
        mobileBefore.Items.Select(item => item.Id).ShouldContain(backlog.Id);
        mobileBefore.Items.Select(item => item.Id).ShouldContain(due.Id);
        var historyBefore = (await client.GetFromJsonAsync<MobileFlightPageDto>($"{mobilePath}/history"))!;
        historyBefore.Items.ShouldHaveSingleItem().Id.ShouldBe(backlog.Id);

        var worker = new FlightAutoCloseBackgroundService(factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new AutoCloseTestClock(now), NullLogger<FlightAutoCloseBackgroundService>.Instance);
        await worker.RunOnceAsync(CancellationToken.None);
        await worker.RunOnceAsync(CancellationToken.None);

        var portalPath = $"{Base}/flights?stationId={refs.StationId}";
        var portal = (await admin.GetFromJsonAsync<PagedList<FlightListItemDto>>($"{portalPath}&pageSize=100"))!;
        var closed = portal.Items.Where(item => item.Id == backlog.Id || item.Id == due.Id).ToArray();
        closed.Length.ShouldBe(2);
        closed.ShouldAllBe(item => item.Status == "AutoClosed" && item.ApprovalStatus == FlightApprovalStatus.Approved &&
            item.ApprovedByDisplayName == "System" && item.ApprovedAtUtc == now && !item.HasOperationalRecords);
        closed.Select(item => item.WorkOrderNumber).ShouldBe(["TST-0002", "TST-0003"], ignoreOrder: true);
        var unchanged = new[] { tooSoon.Id, regular.Id, withArrival.Id, withService.Id, withTask.Id, canceled.Id };
        var untouched = portal.Items.Where(item => unchanged.Contains(item.Id)).ToArray();
        untouched.Length.ShouldBe(unchanged.Length);
        untouched.ShouldAllBe(item =>
            item.ApprovalStatus == FlightApprovalStatus.NotApproved && item.WorkOrderNumber == null);
        var filtered = (await admin.GetFromJsonAsync<PagedList<FlightListItemDto>>($"{portalPath}&status=AutoClosed"))!;
        filtered.Items.Select(item => item.Id).ShouldBe(hiddenIds, ignoreOrder: true);

        var mobileAfter = (await client.GetFromJsonAsync<MobileFlightPageDto>($"{mobilePath}?category=PerLanding"))!;
        mobileAfter.Items.Select(item => item.Id).ShouldBe(
            [tooSoon.Id, withArrival.Id, withService.Id, withTask.Id, canceled.Id, manuallyApproved.Id], ignoreOrder: true);
        using var detail = await client.GetAsync($"{mobilePath}/{backlog.Id}");
        detail.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var batchResponse = await client.PostAsJsonAsync($"{mobilePath}/details/batch",
            new MobileFlightDetailsBatchRequest([.. hiddenIds, manuallyApproved.Id]));
        batchResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var batch = (await batchResponse.Content.ReadFromJsonAsync<MobileFlightDetailsBatchDto>())!;
        batch.MissingFlightIds.ShouldBe(hiddenIds, ignoreOrder: true);
        batch.Items.ShouldHaveSingleItem().Id.ShouldBe(manuallyApproved.Id);
        var history = (await client.GetFromJsonAsync<MobileFlightPageDto>($"{mobilePath}/history"))!;
        history.Items.ShouldNotContain(item => item.Id == backlog.Id);
        using var targets = await client.PostAsJsonAsync($"{mobilePath}/notification-targets",
            new { flightIds = new[] { backlog.Id, manuallyApproved.Id } });
        targets.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await targets.Content.ReadFromJsonAsync<Guid[]>()).ShouldBe([manuallyApproved.Id]);

        var order = await PortalOperationsDetailAsync(admin, backlog.Id);
        using var unlock = await WritePortalOperationsAsync(admin, backlog.Id, HttpMethod.Delete, "approval", null, order.RowVersion);
        unlock.StatusCode.ShouldBe(HttpStatusCode.OK);
        await worker.RunOnceAsync(CancellationToken.None);
        var unlocked = await PortalOperationsDetailAsync(admin, backlog.Id);
        unlocked.ApprovalStatus.ShouldBe(FlightApprovalStatus.Unlocked);
        unlocked.WorkOrderNumber.ShouldBeNull();
        using var restored = await client.GetAsync($"{mobilePath}/{backlog.Id}");
        restored.StatusCode.ShouldBe(HttpStatusCode.OK);

        Flight MakeFlight(string number, DateTimeOffset std, bool perLanding = true) => Flight.ScheduleNew(
            new(refs.CustomerId, null, "Auto-close customer"), new(refs.StationId, "TST", "Station"),
            new(refs.OperationTypeId, "Transit"), FlightNumber.Create(number).Value,
            ScheduledTime.Create(std.AddHours(-1), std).Value, null,
            [perLanding ? new(WellKnownMasterDataIds.AircraftPerLandingService, "Aircraft Per Landing") : new(refs.ServiceId, "Service")],
            [], userId, now).Value;
    }

    private sealed class AutoCloseTestClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
