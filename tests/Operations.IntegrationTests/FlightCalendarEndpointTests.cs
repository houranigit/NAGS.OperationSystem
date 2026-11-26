using System.Net;
using System.Net.Http.Json;
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
    public async Task Calendar_uses_the_scheduled_arrival_range_and_portal_flight_scope()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var otherStationId = await CreateStationAsync(admin, refs.CountryId);
        var dispatcher = await CreateStaffLoginAsync(admin, refs, DispatcherPermissions);
        var regular = await CreateStaffLoginAsync(admin, refs, RegularStaffPermissions);
        using var dispatcherClient = dispatcher.Client;
        using var regularClient = regular.Client;

        var actor = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var fromUtc = new DateTimeOffset(now.UtcDateTime.Date.AddDays(2), TimeSpan.Zero);
        var toUtc = fromUtc.AddDays(1);
        Flight Create(string number, DateTimeOffset arrival, Guid stationId, Guid customerId,
            bool assigned = false, bool perLanding = false) => Flight.ScheduleNew(
                new CustomerSnapshot(customerId, null, "Calendar customer"),
                new StationSnapshot(stationId, "TST", "Calendar station"),
                new OperationTypeSnapshot(refs.OperationTypeId, "Transit"),
                FlightNumber.Create(number).Value, ScheduledTime.Create(arrival, arrival.AddHours(2)).Value, null,
                [new ServiceSnapshot(perLanding ? WellKnownMasterDataIds.AircraftPerLandingService : refs.ServiceId, "Service")],
                assigned ? [new StaffMemberSnapshot(regular.StaffId, "Assigned reader", "CAL1")] : [],
                actor, now).Value;

        var carriesOver = Create("4300", fromUtc.AddMinutes(-1), refs.StationId, refs.CustomerId);
        var first = Create("4301", fromUtc, refs.StationId, refs.CustomerId, assigned: true);
        var canceled = Create("4302", fromUtc.AddHours(1), refs.StationId, refs.CustomerId);
        var perLanding = Create("4303", fromUtc.AddHours(2), refs.StationId, refs.CustomerId, perLanding: true);
        var anotherCustomer = Create("4304", fromUtc.AddHours(3), refs.StationId, WellKnownMasterDataIds.UnknownCustomer);
        var anotherStation = Create("4305", fromUtc.AddHours(4), otherStationId, refs.CustomerId);
        var endBoundary = Create("4306", toUtc, refs.StationId, refs.CustomerId);
        var firstReporter = new StaffMemberSnapshot(regular.StaffId, "First reporter", "CAL1");
        var secondReporter = new StaffMemberSnapshot(dispatcher.StaffId, "Second reporter", "CAL2");
        canceled.SubmitCancellation(firstReporter, CancellationDetails.Create(now, null).Value, actor, now).IsSuccess.ShouldBeTrue();
        canceled.Approve(actor, "Calendar reviewer", now, 1).IsSuccess.ShouldBeTrue();

        var aircraft = new AircraftTypeSnapshot(Guid.NewGuid(), "Airbus", "A320");
        var actualArrival = fromUtc.AddHours(2).AddMinutes(-5);
        var actualDeparture = fromUtc.AddHours(4).AddMinutes(20);
        perLanding.SubmitArrival(firstReporter,
            ArrivalDetails.Create(null, actualArrival.AddMinutes(10), aircraft, "HZ-TEST", firstReporter, null).Value,
            actor, now).IsSuccess.ShouldBeTrue();
        var acceptedArrival = perLanding.SubmitArrival(secondReporter,
            ArrivalDetails.Create(null, actualArrival, aircraft, "HZ-TEST", secondReporter, null).Value,
            actor, now.AddSeconds(1)).Value;
        perLanding.SubmitDeparture(firstReporter,
            DepartureDetails.Create(actualDeparture, null, firstReporter, null).Value, actor, now).IsSuccess.ShouldBeTrue();
        perLanding.SubmitDeparture(secondReporter,
            DepartureDetails.Create(actualDeparture.AddMinutes(-10), null, secondReporter, null).Value,
            actor, now.AddSeconds(1)).IsSuccess.ShouldBeTrue();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            db.Flights.AddRange(carriesOver, first, canceled, perLanding, anotherCustomer, anotherStation, endBoundary);
            // Seed a later accepted arrival alongside an earlier alternative to exercise read precedence.
            db.Entry(acceptedArrival).Property(submission => submission.IsAccepted).CurrentValue = true;
            await db.SaveChangesAsync();
        }

        static string RangePath(DateTimeOffset from, DateTimeOffset to) =>
            $"{Base}/flights/calendar?fromUtc={Uri.EscapeDataString(from.ToString("O"))}&toUtc={Uri.EscapeDataString(to.ToString("O"))}";

        var path = RangePath(fromUtc, toUtc);
        var customerPath = $"{path}&customerId={refs.CustomerId}";
        var all = (await admin.GetFromJsonAsync<CalendarFlightDto[]>(customerPath))!;
        all.Select(flight => flight.Id).ShouldBe([first.Id, canceled.Id, perLanding.Id, anotherStation.Id]);
        all[0].ScheduledArrivalUtc.ShouldBe(fromUtc);
        all[0].StationTimeZoneId.ShouldBe("Etc/UTC");
        all[0].Status.ShouldBe(nameof(FlightReportStatus.Scheduled));
        all[0].ApprovalStatus.ShouldBe(FlightApprovalStatus.NotApproved);
        all[0].ActualArrivalUtc.ShouldBeNull();
        all[0].ActualDepartureUtc.ShouldBeNull();
        all[1].Status.ShouldBe(nameof(FlightReportStatus.Canceled));
        all[1].ApprovalStatus.ShouldBe(FlightApprovalStatus.Approved);
        all[2].IsPerLanding.ShouldBeTrue();
        all[2].IsOnCall.ShouldBeFalse();
        all[2].ActualArrivalUtc.ShouldBe(actualArrival);
        all[2].ActualDepartureUtc.ShouldBe(actualDeparture);

        var station = (await admin.GetFromJsonAsync<CalendarFlightDto[]>($"{path}&stationId={refs.StationId}"))!;
        station.Select(flight => flight.Id).ShouldBe([first.Id, canceled.Id, perLanding.Id, anotherCustomer.Id]);
        var filtered = (await admin.GetFromJsonAsync<CalendarFlightDto[]>($"{customerPath}&stationId={refs.StationId}"))!;
        filtered.Select(flight => flight.Id).ShouldBe([first.Id, canceled.Id, perLanding.Id]);

        var dispatch = (await dispatcherClient.GetFromJsonAsync<CalendarFlightDto[]>(
            $"{customerPath}&stationId={otherStationId}"))!;
        dispatch.Select(flight => flight.Id).ShouldBe([first.Id, canceled.Id, perLanding.Id]);
        var assigned = (await regularClient.GetFromJsonAsync<CalendarFlightDto[]>(customerPath))!;
        assigned.Select(flight => flight.Id).ShouldBe([first.Id, perLanding.Id]);

        using var oversized = await admin.GetAsync(RangePath(fromUtc, fromUtc.AddDays(63)));
        oversized.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
