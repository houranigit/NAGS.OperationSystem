using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.Application.Pagination;
using MasterData.Contracts.Seeding;
using Microsoft.Extensions.DependencyInjection;
using Operations.Application.Contracts;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed partial class FlightVisibilityScopeTests
{
    [Fact]
    public async Task Analytics_dashboard_uses_current_flight_scope_lifecycle_and_shared_export_filters()
    {
        const string dashboardPath = $"{Base}/analytics-dashboard";
        const string viewPermission = "operations.dashboard.view-analytics";
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var otherStation = await CreateStationAsync(admin, refs.CountryId);
        var staff = await CreateStaffLoginAsync(admin, refs, [viewPermission]);
        using var staffClient = staff.Client;
        var denied = await CreateStaffLoginAsync(admin, refs, RegularStaffPermissions);
        using var deniedClient = denied.Client;

        var now = DateTimeOffset.UtcNow;
        var from = new DateTimeOffset(now.UtcDateTime.Date.AddDays(2).AddHours(20).AddMinutes(30), TimeSpan.Zero);
        var to = from.AddDays(1);
        var actor = Guid.NewGuid();
        StaffMemberSnapshot Employee() => new(staff.StaffId, "Dashboard operator", "DASH1");
        Flight Create(string number, DateTimeOffset arrival, Guid station, bool assigned = false, bool perLanding = false) =>
            Flight.ScheduleNew(new CustomerSnapshot(refs.CustomerId, null, "Dashboard customer"),
                new StationSnapshot(station, "TST", "Dashboard station"), new OperationTypeSnapshot(refs.OperationTypeId, "Transit"),
                FlightNumber.Create(number).Value, ScheduledTime.Create(arrival, arrival.AddHours(2)).Value, null,
                [new ServiceSnapshot(perLanding ? WellKnownMasterDataIds.AircraftPerLandingService : refs.ServiceId, "Planned service")],
                assigned ? [Employee()] : [], actor, now).Value;

        var scheduled = Create("6801", from, refs.StationId, assigned: true);
        var onCall = Create("6802", from.AddHours(1), refs.StationId, perLanding: true);
        var perLanding = Create("6803", from.AddHours(2), refs.StationId, perLanding: true);
        var unassigned = Create("6804", from.AddHours(3), refs.StationId);
        var external = Create("6805", from.AddHours(4), otherStation);
        var endBoundary = Create("6806", to, refs.StationId, assigned: true);

        var aircraft = new AircraftTypeSnapshot(Guid.NewGuid(), "Airbus", "A320");
        onCall.SubmitArrival(Employee(), ArrivalDetails.Create(null, from.AddHours(1), aircraft, "HZ-DASH", Employee(), null).Value,
            actor, now).IsSuccess.ShouldBeTrue();
        for (var index = 0; index < 2; index++)
        {
            var start = from.AddHours(1).AddMinutes(10 * index);
            var window = TimeWindow.Create(start, start.AddMinutes(5)).Value;
            onCall.SaveService(Guid.NewGuid(), new ServiceSnapshot(refs.ServiceId, "Performed service"), null, window, null,
                [new FlightServicePerformerInput(Employee(), TimeWindow.Create(window.FromUtc, window.ToUtc).Value, Guid.NewGuid())],
                actor, now).IsSuccess.ShouldBeTrue();
        }
        await using (var dbScope = factory.Services.CreateAsyncScope())
        {
            var db = dbScope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            db.Flights.AddRange(scheduled, onCall, perLanding, unassigned, external, endBoundary);
            await db.SaveChangesAsync();
        }

        var filter = $"fromUtc={Uri.EscapeDataString(from.ToString("O"))}&toUtc={Uri.EscapeDataString(to.ToString("O"))}&customerIds={refs.CustomerId}";
        var zoneFilter = Uri.EscapeDataString("Browser UTC+03:30");
        var data = await staffClient.GetFromJsonAsync<OperationsDashboardDto>($"{dashboardPath}?{filter}&timeZoneId={zoneFilter}");
        data.ShouldNotBeNull();
        data.TotalFlights.ShouldBe(3);
        data.Statuses.Single(item => item.Status == nameof(FlightReportStatus.Scheduled)).FlightCount.ShouldBe(2);
        data.Statuses.Single(item => item.Status == nameof(FlightReportStatus.OnGround)).FlightCount.ShouldBe(1);
        data.ServiceCategories.Single(item => item.Label == "Per Landing").FlightCount.ShouldBe(1);
        data.ServiceCategories.Single(item => item.Label == "On Call").FlightCount.ShouldBe(1);
        data.FlightsWithPerformedServices.ShouldBe(1);
        data.Services.ShouldHaveSingleItem().FlightCount.ShouldBe(1);
        data.StationOptions.ShouldHaveSingleItem().Id.ShouldBe(refs.StationId);
        data.TimelineGranularity.ShouldBe("Hour");
        data.Timeline.Count.ShouldBe(24);
        data.Timeline[0].BucketUtc.ShouldBe(from);
        data.Timeline[1].FlightCount.ShouldBe(1);

        var performedFilter = $"{filter}&serviceIds={refs.ServiceId}";
        var table = await staffClient.GetFromJsonAsync<PagedResult<DashboardFlightRowDto>>(
            $"{dashboardPath}/flights?{performedFilter}&pageSize=1&sort=status:desc");
        table.ShouldNotBeNull();
        table.TotalCount.ShouldBe(1);
        var row = table.Items.ShouldHaveSingleItem();
        row.Id.ShouldBe(onCall.Id);
        row.PerformedServiceNames.ShouldBe(["Performed service"]);
        row.WorkOrderStatus.ShouldBe(nameof(FlightApprovalStatus.NotApproved));

        using var deniedRead = await deniedClient.GetAsync($"{dashboardPath}?{filter}");
        deniedRead.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var deniedExport = await staffClient.GetAsync($"{dashboardPath}/flights/export?format=csv&{performedFilter}");
        deniedExport.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var export = await admin.GetAsync($"{dashboardPath}/flights/export?format=csv&{performedFilter}");
        export.StatusCode.ShouldBe(HttpStatusCode.OK);
        var csv = await export.Content.ReadAsStringAsync();
        csv.ShouldContain("6802");
        csv.ShouldNotContain("6801");
        csv.ShouldNotContain("6806");

        using var invalid = await admin.GetAsync($"{dashboardPath}?serviceIds=not-a-guid");
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
