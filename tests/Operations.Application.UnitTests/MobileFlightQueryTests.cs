using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Mobile;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Authorization;
using Operations.Application.Features.Mobile;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class MobileFlightQueryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 18, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Lists_return_only_their_members_and_supported_statuses_within_the_window()
    {
        await using var db = NewDb();
        var stationId = Guid.NewGuid();
        var staff = new StaffMemberSnapshot(Guid.NewGuid(), "Mobile Staff", "EMP-100");

        var myScheduled = CreateFlight("MY100", stationId, assigned: [staff]);
        var myInProgress = CreateFlight("MY200", stationId, assigned: [staff]);
        myInProgress.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        var myCompleted = CreateFlight("MY300", stationId, assigned: [staff]);
        Complete(myCompleted);
        var myCanceled = CreateFlight("MY400", stationId, assigned: [staff]);
        Cancel(myCanceled);
        var myOutsideWindow = CreateFlight(
            "MY500", stationId, assigned: [staff], sta: Now.AddHours(13));

        // Assigned Ad Hoc flights (including invitations) remain exclusively in Ad Hoc.
        var adHocScheduled = CreateFlight("AH100", stationId, isAdHoc: true, assigned: [staff]);
        var adHocInProgress = CreateFlight("AH200", stationId, isAdHoc: true, assigned: [staff]);
        adHocInProgress.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        var adHocCompleted = CreateFlight("AH300", stationId, isAdHoc: true, assigned: [staff]);
        Complete(adHocCompleted);
        var adHocCanceled = CreateFlight("AH400", stationId, isAdHoc: true, assigned: [staff]);
        Cancel(adHocCanceled);

        var perLandingScheduled = CreateFlight("PL100", stationId, isPerLanding: true);
        var perLandingInProgress = CreateFlight("PL200", stationId, isPerLanding: true);
        perLandingInProgress.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        var perLandingCompleted = CreateFlight("PL300", stationId, isPerLanding: true);
        Complete(perLandingCompleted);
        var perLandingCanceled = CreateFlight("PL400", stationId, isPerLanding: true);
        Cancel(perLandingCanceled);

        // Even the unusual overlapping shape is Ad Hoc-only.
        var adHocPerLanding = CreateFlight(
            "AHPL100", stationId, isAdHoc: true, isPerLanding: true);

        db.Flights.AddRange(
            myScheduled, myInProgress, myCompleted, myCanceled, myOutsideWindow,
            adHocScheduled, adHocInProgress, adHocCompleted, adHocCanceled,
            perLandingScheduled, perLandingInProgress, perLandingCompleted, perLandingCanceled,
            adHocPerLanding);
        await db.SaveChangesAsync();

        var handler = Handler(db, stationId, staff.StaffMemberId);
        var my = await handler.Handle(new GetMobileFlightsQuery(MobileFlightList.My), CancellationToken.None);
        var perLanding = await handler.Handle(
            new GetMobileFlightsQuery(MobileFlightList.PerLanding), CancellationToken.None);
        var adHoc = await handler.Handle(
            new GetMobileFlightsQuery(MobileFlightList.AdHoc), CancellationToken.None);

        my.IsSuccess.ShouldBeTrue();
        my.Value.Select(flight => flight.Id).ShouldBe(
            [myScheduled.Id, myInProgress.Id, myCompleted.Id], ignoreOrder: true);
        perLanding.IsSuccess.ShouldBeTrue();
        perLanding.Value.Select(flight => flight.Id).ShouldBe(
            [perLandingScheduled.Id, perLandingInProgress.Id, perLandingCompleted.Id],
            ignoreOrder: true);
        adHoc.IsSuccess.ShouldBeTrue();
        adHoc.Value.Select(flight => flight.Id).ShouldBe(
            [adHocScheduled.Id, adHocInProgress.Id, adHocCompleted.Id, adHocPerLanding.Id],
            ignoreOrder: true);
    }

    [Fact]
    public async Task Unpaginated_mobile_snapshot_returns_every_in_window_flight()
    {
        await using var db = NewDb();
        var stationId = Guid.NewGuid();
        var staff = new StaffMemberSnapshot(Guid.NewGuid(), "Mobile Staff", "EMP-100");
        var flights = Enumerable.Range(1, 125)
            .Select(number => CreateFlight($"M{number:000}", stationId, assigned: [staff]))
            .ToList();
        db.Flights.AddRange(flights);
        await db.SaveChangesAsync();

        var result = await Handler(db, stationId, staff.StaffMemberId).Handle(
            new GetMobileFlightsQuery(MobileFlightList.My), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(125);
        result.Value.Select(flight => flight.Id).ShouldBe(
            flights.Select(flight => flight.Id), ignoreOrder: true);
    }

    [Fact]
    public async Task By_id_allows_same_station_ad_hoc_realtime_fetch_without_broadening_regular_access()
    {
        await using var db = NewDb();
        var stationId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var adHoc = CreateFlight("AH600", stationId, isAdHoc: true);
        var regular = CreateFlight("MY700", stationId);
        var otherStationAdHoc = CreateFlight("AH700", Guid.NewGuid(), isAdHoc: true);
        db.Flights.AddRange(adHoc, regular, otherStationAdHoc);
        await db.SaveChangesAsync();

        var scope = new StaticScope(new OperationsScopeContext(UserType.StationStaff, stationId, staffId));
        var handler = new GetMobileFlightByIdQueryHandler(
            db, scope, new StaticUserContext(), new StaticTimeProvider(Now));

        var adHocResult = await handler.Handle(
            new GetMobileFlightByIdQuery(adHoc.Id), CancellationToken.None);
        var regularResult = await handler.Handle(
            new GetMobileFlightByIdQuery(regular.Id), CancellationToken.None);
        var otherStationResult = await handler.Handle(
            new GetMobileFlightByIdQuery(otherStationAdHoc.Id), CancellationToken.None);

        adHocResult.IsSuccess.ShouldBeTrue();
        adHocResult.Value.Id.ShouldBe(adHoc.Id);
        regularResult.IsFailure.ShouldBeTrue();
        otherStationResult.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Realtime_envelopes_purge_nonmembers_and_keep_completed_flights()
    {
        var stationId = Guid.NewGuid();
        var staff = new StaffMemberSnapshot(Guid.NewGuid(), "Mobile Staff", "EMP-100");
        var broadcaster = new RecordingBroadcaster();

        var adHocCompleted = CreateFlight("AH500", stationId, isAdHoc: true, assigned: [staff]);
        Complete(adHocCompleted);
        MobileFlightSync.EnqueueUpsert(broadcaster, adHocCompleted);

        broadcaster.Changes.Single(change => change.Table == MobileSyncTables.Flights)
            .Op.ShouldBe(MobileSyncOps.Delete);
        broadcaster.Changes.Single(change => change.Table == MobileSyncTables.FlightsAdHoc)
            .Op.ShouldBe(MobileSyncOps.Upsert);

        broadcaster.Changes.Clear();
        var canceled = CreateFlight("MY600", stationId, assigned: [staff]);
        Cancel(canceled);
        MobileFlightSync.EnqueueUpsert(broadcaster, canceled);

        broadcaster.Changes.ShouldHaveSingleItem();
        broadcaster.Changes[0].Table.ShouldBe(MobileSyncTables.Flights);
        broadcaster.Changes[0].Op.ShouldBe(MobileSyncOps.Delete);
    }

    private static GetMobileFlightsQueryHandler Handler(
        OperationsDbContext db,
        Guid stationId,
        Guid staffMemberId) =>
        new(
            db,
            new StaticScope(new OperationsScopeContext(UserType.StationStaff, stationId, staffMemberId)),
            new StaticUserContext(),
            new StaticTimeProvider(Now));

    private static OperationsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OperationsDbContext>()
            .UseInMemoryDatabase($"mobile-flights-{Guid.NewGuid()}")
            .Options);

    private static Flight CreateFlight(
        string number,
        Guid stationId,
        bool isAdHoc = false,
        bool isPerLanding = false,
        IReadOnlyList<StaffMemberSnapshot>? assigned = null,
        DateTimeOffset? sta = null)
    {
        var plannedService = isPerLanding
            ? new ServiceSnapshot(WellKnownMasterDataIds.AircraftPerLandingService, "Aircraft Per Landing")
            : new ServiceSnapshot(Guid.NewGuid(), "Marshalling");
        var operationType = isAdHoc
            ? new OperationTypeSnapshot(WellKnownMasterDataIds.AdHocOperationType, "Ad Hoc")
            : new OperationTypeSnapshot(Guid.NewGuid(), "Transit");
        var arrival = sta ?? Now;

        return Flight.ScheduleNew(
            new CustomerSnapshot(Guid.NewGuid(), "SV", "Saudia"),
            new StationSnapshot(stationId, "DMM", "Dammam"),
            operationType,
            FlightNumber.Create(number).Value,
            ScheduledTime.Create(arrival, arrival.AddHours(1)).Value,
            aircraftType: null,
            plannedServices: [plannedService],
            // EF owned snapshots cannot be shared between aggregate instances in the in-memory
            // provider; mirror production hydration with one value-object instance per row.
            assignedEmployees: assigned?
                .Select(employee => new StaffMemberSnapshot(
                    employee.StaffMemberId,
                    employee.FullName,
                    employee.EmployeeId))
                .ToList() ?? [],
            contractId: null,
            contractNumber: null,
            createdByUserId: Guid.NewGuid(),
            now: Now).Value;
    }

    private static void Complete(Flight flight)
    {
        flight.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        flight.SettleCompleted(Now).IsSuccess.ShouldBeTrue();
    }

    private static void Cancel(Flight flight)
    {
        flight.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        flight.SettleCanceled(Now).IsSuccess.ShouldBeTrue();
    }

    private sealed class StaticScope(OperationsScopeContext context) : IOperationsScope
    {
        public Task<Result<OperationsScopeContext>> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(context));
    }

    private sealed class StaticUserContext : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.StationStaff;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => true;
    }

    private sealed class StaticTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingBroadcaster : IMobileSyncBroadcaster
    {
        public List<MobileSyncChange> Changes { get; } = [];
        public void Enqueue(MobileSyncChange change) => Changes.Add(change);
        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task BroadcastNowAsync(
            MobileSyncChange change,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
