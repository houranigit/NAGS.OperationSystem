using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Features.Flights;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class MobileFlightTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid StationId = Guid.NewGuid();
    private static readonly Guid StaffId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly MobileFlightScopeContext Context = new(StationId, StaffId, UserId, Now);

    [Fact]
    public async Task MobileReads_RejectAnOrphanStationInsteadOfReturningMissingTimeZoneMetadata()
    {
        await using var db = NewDb();
        var flight = FlightFor("100", assigned: true, sta: Now.AddHours(-1), std: Now.AddHours(1));
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        var reader = new SchedulingMasterDataReader { StationExists = false };

        var page = await new GetMobileFlightsQueryHandler(db, Scope(), NoopCustomerLogoReader.Instance, reader)
            .Handle(new GetMobileFlightsQuery(), CancellationToken.None);
        var detail = await new GetMobileFlightByIdQueryHandler(db, Scope(), reader)
            .Handle(new GetMobileFlightByIdQuery(flight.Id), CancellationToken.None);

        page.IsFailure.ShouldBeTrue();
        detail.IsFailure.ShouldBeTrue();
        page.Error.Code.ShouldBe("Operations.Flight.StationDataUnavailable");
        detail.Error.Code.ShouldBe("Operations.Flight.StationDataUnavailable");
    }

    [Fact]
    public async Task Detail_LoadsOrderedDepartureSubmissionsWithIndependentHandlingEmployeeSnapshots()
    {
        await using var db = NewDb();
        var flight = FlightFor("100", assigned: true);
        var submitter = new StaffMemberSnapshot(Guid.NewGuid(), "Submitting employee", "S1");
        var handler = new StaffMemberSnapshot(Guid.NewGuid(), "Handling employee", "H1");
        var signature = CustomerSignature.Create("signature.png", "signatures/read-test.png", "image/png", 100, Now).Value;
        var first = flight.SubmitDeparture(submitter,
            DepartureDetails.Create(Now.AddMinutes(10), signature, handler, "First departure").Value,
            UserId, Now.AddMinutes(1)).Value;
        var second = flight.SubmitDeparture(new StaffMemberSnapshot(Guid.NewGuid(), "Other submitter", "S2"),
            DepartureDetails.Create(Now.AddMinutes(20), signature, handler, null).Value,
            UserId, Now.AddMinutes(2)).Value;
        flight.SubmitDeparture(submitter,
            DepartureDetails.Create(Now.AddMinutes(15), signature, handler, "Corrected departure").Value,
            UserId, Now.AddMinutes(3)).IsSuccess.ShouldBeTrue();
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new GetMobileFlightByIdQueryHandler(db, Scope(), new SchedulingMasterDataReader(), new Actor())
            .Handle(new GetMobileFlightByIdQuery(flight.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CanSaveDeparture.ShouldBeTrue();
        var departures = result.Value.DepartureSubmissions!;
        departures.Select(item => item.Id).ShouldBe([first.Id, second.Id]);
        departures[0].Atd.ShouldBe(Now.AddMinutes(15));
        departures[0].SubmittedByEmployee.StaffMemberId.ShouldBe(submitter.StaffMemberId);
        departures[0].HandledByEmployee.StaffMemberId.ShouldBe(handler.StaffMemberId);
        departures[0].HandledByEmployee.FullName.ShouldBe(handler.FullName);
        departures[0].Remarks.ShouldBe("Corrected departure");
        departures[0].CreatedAtUtc.ShouldBe(Now.AddMinutes(1));
        departures[0].UpdatedAtUtc.ShouldBe(Now.AddMinutes(3));
        departures.ShouldAllBe(item => !item.IsAccepted);
        db.ChangeTracker.Entries<Flight>().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(UserType.SystemAdministrator)]
    [InlineData(UserType.ViewerOnly)]
    public async Task MobileScope_DeniesUnlinkedPortalReaders(UserType userType)
    {
        var scope = new MobileFlightScope(new OperationsScope(new Actor(userType), null!),
            new Actor(userType), new Clock());

        var result = await scope.ResolveAsync(CancellationToken.None);

        result.Error.Code.ShouldBe("Operations.Mobile.StaffLinkRequired");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MobileScope_PreservesMissingOrInactiveStaffDenial(bool inactive)
    {
        var reader = new SchedulingMasterDataReader();
        if (inactive)
            reader.StaffMembers[StaffId] = new(StaffId, "Staff", "E1", StationId, Guid.NewGuid(), false);
        var user = new Actor();
        var scope = new MobileFlightScope(new OperationsScope(user, reader), user, new Clock());

        var result = await scope.ResolveAsync(CancellationToken.None);

        result.Error.Code.ShouldBe("Operations.Scope.Denied");
    }

    [Fact]
    public async Task MobileScope_UsesActiveStaffStationAndServerClock()
    {
        var reader = new SchedulingMasterDataReader();
        reader.StaffMembers[StaffId] = new(StaffId, "Staff", "E1", StationId, Guid.NewGuid(), true);
        var user = new Actor();

        var result = await new MobileFlightScope(new OperationsScope(user, reader), user, new Clock())
            .ResolveAsync(CancellationToken.None);

        result.Value.ShouldBe(Context);
        result.Value.WindowStartUtc.ShouldBe(Now.AddHours(-12));
        result.Value.WindowEndUtc.ShouldBe(Now.AddHours(12));
    }

    [Theory]
    [InlineData(null, 4)]
    [InlineData("MyFlights", 1)]
    [InlineData("PerLanding", 1)]
    [InlineData("AdHoc", 2)]
    public async Task Feed_UsesAssignmentCreatorAndAdHocFirstCategories(string? category, int count)
    {
        await using var db = NewDb();
        var mine = FlightFor("100", assigned: true);
        var landing = FlightFor("200", perLanding: true);
        var ownAdHoc = FlightFor("300", adHoc: true);
        var ownMixed = FlightFor("400", perLanding: true, adHoc: true);
        var otherUser = Guid.NewGuid();
        db.Flights.AddRange(mine, landing, ownAdHoc, ownMixed,
            FlightFor("500"), // A station dispatcher still cannot widen My Flights.
            FlightFor("600", assigned: true, adHoc: true, createdBy: otherUser),
            FlightFor("700", perLanding: true, adHoc: true, createdBy: otherUser),
            FlightFor("800", assigned: true, stationId: Guid.NewGuid()));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new GetMobileFlightsQueryHandler(db, Scope(), NoopCustomerLogoReader.Instance, new SchedulingMasterDataReader())
            .Handle(new GetMobileFlightsQuery(Category: category), CancellationToken.None);

        result.Value.Items.Count.ShouldBe(count);
        result.Value.TotalCount.ShouldBe(count);
        result.Value.StationId.ShouldBe(StationId);
        result.Value.AsOfUtc.ShouldBe(Now);
        result.Value.WindowStartUtc.ShouldBe(Now.AddHours(-12));
        result.Value.WindowEndUtc.ShouldBe(Now.AddHours(12));
        result.Value.Items.ShouldAllBe(f => f.StationId == StationId);
        if (category is null)
        {
            result.Value.Items.Single(f => f.Id == mine.Id).IsMyFlight.ShouldBeTrue();
            result.Value.Items.Single(f => f.Id == landing.Id).IsMyFlight.ShouldBeFalse();
            result.Value.Items.Single(f => f.Id == ownAdHoc.Id).IsMyAdHocFlight.ShouldBeTrue();
            var mixed = result.Value.Items.Single(f => f.Id == ownMixed.Id);
            mixed.IsPerLanding.ShouldBeTrue();
            mixed.IsMyAdHocFlight.ShouldBeTrue();
            mixed.IsMyFlight.ShouldBeFalse();
        }
        if (category == "PerLanding")
            result.Value.Items.Single().Id.ShouldBe(landing.Id);
        db.ChangeTracker.Entries<Flight>().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(-15, -12, true)]
    [InlineData(12, 15, true)]
    [InlineData(-30, 30, true)]
    [InlineData(0, 0, true)]
    [InlineData(-14, -13, false)]
    [InlineData(13, 14, false)]
    public async Task FeedAndDetail_UseInclusiveArrivalDepartureOverlap(int arrivalHours, int departureHours, bool visible)
    {
        await using var db = NewDb();
        var flight = FlightFor("100", assigned: true,
            sta: Now.AddHours(arrivalHours), std: Now.AddHours(departureHours));
        db.Flights.Add(flight);
        await db.SaveChangesAsync();

        var list = await new GetMobileFlightsQueryHandler(db, Scope(), NoopCustomerLogoReader.Instance, new SchedulingMasterDataReader())
            .Handle(new GetMobileFlightsQuery(), CancellationToken.None);
        var detail = await new GetMobileFlightByIdQueryHandler(db, Scope(), new SchedulingMasterDataReader())
            .Handle(new GetMobileFlightByIdQuery(flight.Id), CancellationToken.None);

        list.Value.Items.Count.ShouldBe(visible ? 1 : 0);
        detail.IsSuccess.ShouldBe(visible);
        if (!visible)
            detail.Error.Type.ShouldBe(ErrorType.NotFound);
        else
        {
            detail.Value.StationTimeZoneId.ShouldBe("Asia/Riyadh");
            list.Value.Items.Single().StationTimeZoneId.ShouldBe("Asia/Riyadh");
        }
    }

    [Theory]
    [InlineData("outdated")]
    [InlineData("other-station")]
    [InlineData("unassigned")]
    [InlineData("others-ad-hoc")]
    public async Task HiddenFlights_CannotBeReadByDetailLogoOrInvitationRoutes(string reason)
    {
        await using var db = NewDb();
        var flight = FlightFor("100", assigned: reason != "unassigned",
            adHoc: reason == "others-ad-hoc", createdBy: Guid.NewGuid(),
            stationId: reason == "other-station" ? Guid.NewGuid() : StationId,
            sta: reason == "outdated" ? Now.AddDays(-2) : Now,
            std: reason == "outdated" ? Now.AddDays(-2).AddHours(1) : Now.AddHours(1));
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        var reader = new SchedulingMasterDataReader();
        var logos = new LogoReader();

        var detail = await new GetMobileFlightByIdQueryHandler(db, Scope(), new SchedulingMasterDataReader())
            .Handle(new GetMobileFlightByIdQuery(flight.Id), CancellationToken.None);
        var logo = await new GetMobileFlightCustomerLogoQueryHandler(db, Scope(), logos)
            .Handle(new GetMobileFlightCustomerLogoQuery(flight.Customer.CustomerId), CancellationToken.None);
        var options = await new GetFlightInvitationOptionsQueryHandler(db, null!, reader, Scope())
            .Handle(new GetFlightInvitationOptionsQuery(flight.Id, MobileOnly: true), CancellationToken.None);
        var invite = await new InviteEmployeesToFlightCommandHandler(db, null!, new MasterDataResolver(reader),
            new Actor(), new AuditActor(), new Clock(), NoopMobileSyncBroadcaster.Instance, Scope())
            .Handle(new InviteEmployeesToFlightCommand(flight.Id, [Guid.NewGuid()], flight.RowVersion, MobileOnly: true), CancellationToken.None);

        detail.Error.Type.ShouldBe(ErrorType.NotFound);
        logo.Error.Type.ShouldBe(ErrorType.NotFound);
        options.Error.Type.ShouldBe(ErrorType.NotFound);
        invite.Error.Type.ShouldBe(ErrorType.NotFound);
        logos.ContentCalls.ShouldBe(0);
        (await db.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Feed_PaginatesFilteredCategoryAndOnlyBatchesPageCustomerLogoVersions()
    {
        await using var db = NewDb();
        var first = FlightFor("100", assigned: true);
        var second = FlightFor("200", assigned: true);
        db.Flights.AddRange(first, second, FlightFor("300", perLanding: true), FlightFor("400"));
        await db.SaveChangesAsync();
        var logos = new LogoReader();
        var handler = new GetMobileFlightsQueryHandler(db, Scope(), logos, new SchedulingMasterDataReader());

        var result = await handler.Handle(new GetMobileFlightsQuery(PageSize: 1,
            Sort: "flightNumber", Category: "MyFlights"), CancellationToken.None);
        var next = await handler.Handle(new GetMobileFlightsQuery(Page: 2, PageSize: 1,
            Sort: "flightNumber", Category: "MyFlights"), CancellationToken.None);

        result.Value.TotalCount.ShouldBe(2);
        result.Value.TotalPages.ShouldBe(2);
        result.Value.HasNextPage.ShouldBeTrue();
        result.Value.Items.Single().Id.ShouldBe(first.Id);
        next.Value.Items.Single().Id.ShouldBe(second.Id);
        logos.VersionBatches.Count.ShouldBe(2);
        logos.VersionBatches[0].ShouldBe([first.Customer.CustomerId]);
        logos.VersionBatches[1].ShouldBe([second.Customer.CustomerId]);
        result.Value.Items.Single().CustomerLogoVersion.ShouldBe("logo-v1");
        logos.ContentCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("0")]
    [InlineData("")]
    public async Task Feed_DeniesUnknownOrNumericCategories(string category)
    {
        await using var db = NewDb();

        var result = await new GetMobileFlightsQueryHandler(db, Scope(), NoopCustomerLogoReader.Instance, new SchedulingMasterDataReader())
            .Handle(new GetMobileFlightsQuery(Category: category), CancellationToken.None);

        result.Error.Code.ShouldBe("Operations.Mobile.InvalidCategory");
    }

    [Fact]
    public async Task VisibleLogo_ValidatesExpectedVersionAfterCurrentFlightBoundary()
    {
        await using var db = NewDb();
        var flight = FlightFor("100", adHoc: true);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        var handler = new GetMobileFlightCustomerLogoQueryHandler(db, Scope(), new LogoReader());

        var current = await handler.Handle(new GetMobileFlightCustomerLogoQuery(flight.Customer.CustomerId, "logo-v1"), CancellationToken.None);
        var stale = await handler.Handle(new GetMobileFlightCustomerLogoQuery(flight.Customer.CustomerId, "old-logo"), CancellationToken.None);

        current.Value.Version.ShouldBe("logo-v1");
        stale.Error.Type.ShouldBe(ErrorType.PreconditionFailed);
    }

    [Theory]
    [InlineData("MyFlights")]
    [InlineData("PerLanding")]
    [InlineData("AdHoc")]
    public async Task Feed_ProjectsNormalCycleWorkForEachMobileCategory(string category)
    {
        await using var db = NewDb();
        var flight = FlightFor("100", assigned: category == "MyFlights",
            perLanding: category == "PerLanding", adHoc: category == "AdHoc");
        flight.AddService(new ServiceSnapshot(Guid.NewGuid(), "Ground power"), null,
            TimeWindow.Create(Now, null).Value, null, UserId, Now).IsSuccess.ShouldBeTrue();
        flight.AddTask(FlightTaskType.Minor, null, null,
            TimeWindow.Create(Now, Now.AddMinutes(5)).Value, null, UserId, Now).IsSuccess.ShouldBeTrue();
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new GetMobileFlightsQueryHandler(db, Scope(), NoopCustomerLogoReader.Instance, new SchedulingMasterDataReader())
            .Handle(new GetMobileFlightsQuery(Category: category), CancellationToken.None);

        var row = result.Value.Items.ShouldHaveSingleItem();
        row.ActiveWorkCount.ShouldBe(1);
        row.CompletedWorkCount.ShouldBe(1);
        row.HasOpenReturnToRamp.ShouldBeFalse();
        row.LatestReturnToRampSequence.ShouldBeNull();
        row.LatestCancellationAtUtc.ShouldBeNull();
        db.ChangeTracker.Entries<Flight>().ShouldBeEmpty();
        db.ChangeTracker.Entries<FlightService>().ShouldBeEmpty();
        db.ChangeTracker.Entries<FlightTask>().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("open-empty", true, 2, 0, 0)]
    [InlineData("open-mixed", true, 2, 1, 1)]
    [InlineData("closed", false, 2, 0, 2)]
    [InlineData("removed-latest", false, 1, 0, 1)]
    public async Task Feed_CountsOnlyTheLatestExistingReturnToRampCycle(
        string scenario, bool open, int sequence, int active, int completed)
    {
        await using var db = NewDb();
        var flight = FlightFor("100", assigned: true);
        var employee = new StaffMemberSnapshot(StaffId, "Assigned staff", "E1");
        var signature = CustomerSignature.Create("signature.png", "signatures/mobile-card.png", "image/png", 100, Now).Value;
        flight.SubmitDeparture(employee, DepartureDetails.Create(Now, signature, employee, null).Value,
            UserId, Now).IsSuccess.ShouldBeTrue();
        flight.AddService(new ServiceSnapshot(Guid.NewGuid(), "Normal work"), null,
            TimeWindow.Create(Now, null).Value, null, UserId, Now).IsSuccess.ShouldBeTrue();
        flight.AddTask(FlightTaskType.Minor, null, null,
            TimeWindow.Create(Now, Now.AddMinutes(5)).Value, null, UserId, Now).IsSuccess.ShouldBeTrue();

        var first = flight.StartReturnToRamp(RtrArrivalDetails.Create(Now.AddMinutes(10), employee, null).Value,
            UserId, Now).Value;
        var firstWindow = TimeWindow.Create(Now.AddMinutes(10), Now.AddMinutes(20)).Value;
        flight.AddService(new ServiceSnapshot(Guid.NewGuid(), "First RTR work"), null, firstWindow, null,
            UserId, Now, first.Id, [new FlightServicePerformerInput(employee, firstWindow)]).IsSuccess.ShouldBeTrue();
        flight.CloseReturnToRamp(first.Id, RtrDepartureDetails.Create(Now.AddMinutes(30), null, employee, null).Value,
            UserId, Now).IsSuccess.ShouldBeTrue();
        var second = flight.StartReturnToRamp(RtrArrivalDetails.Create(Now.AddMinutes(40), employee, null).Value,
            UserId, Now).Value;
        if (scenario is "open-mixed" or "closed")
        {
            var serviceWindow = TimeWindow.Create(Now.AddMinutes(40), open ? null : Now.AddMinutes(50)).Value;
            flight.AddService(new ServiceSnapshot(Guid.NewGuid(), "Second RTR work"), null, serviceWindow, null,
                UserId, Now, second.Id, [new FlightServicePerformerInput(employee, serviceWindow)]).IsSuccess.ShouldBeTrue();
            var taskWindow = TimeWindow.Create(Now.AddMinutes(40), Now.AddMinutes(55)).Value;
            flight.AddTask(FlightTaskType.Minor, null, null, taskWindow, null, UserId, Now, second.Id,
                [new FlightTaskPerformerInput(employee, taskWindow)]).IsSuccess.ShouldBeTrue();
            if (!open)
                flight.CloseReturnToRamp(second.Id,
                    RtrDepartureDetails.Create(Now.AddMinutes(60), null, employee, null).Value, UserId, Now).IsSuccess.ShouldBeTrue();
        }
        if (scenario == "removed-latest")
            flight.RemoveReturnToRamp(second.Id, UserId, Now).IsSuccess.ShouldBeTrue();
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new GetMobileFlightsQueryHandler(db, Scope(), NoopCustomerLogoReader.Instance, new SchedulingMasterDataReader())
            .Handle(new GetMobileFlightsQuery(), CancellationToken.None);

        var row = result.Value.Items.ShouldHaveSingleItem();
        row.HasDepartureSubmission.ShouldBeTrue();
        row.HasOpenReturnToRamp.ShouldBe(open);
        row.LatestReturnToRampSequence.ShouldBe(sequence);
        row.ActiveWorkCount.ShouldBe(active);
        row.CompletedWorkCount.ShouldBe(completed);
        db.ChangeTracker.Entries<Flight>().ShouldBeEmpty();
        db.ChangeTracker.Entries<FlightReturnToRamp>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Feed_ProjectsLatestReportedCancellationTimeWithoutInferringApproval()
    {
        await using var db = NewDb();
        var flight = FlightFor("100", assigned: true);
        flight.SubmitCancellation(new StaffMemberSnapshot(StaffId, "Assigned staff", "E1"),
            CancellationDetails.Create(Now.AddMinutes(-10), null).Value, UserId, Now).IsSuccess.ShouldBeTrue();
        flight.SubmitCancellation(new StaffMemberSnapshot(Guid.NewGuid(), "Other staff", "E2"),
            CancellationDetails.Create(Now.AddMinutes(-30), null).Value, UserId, Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        flight.CancellationSubmissions.ShouldAllBe(item => !item.IsAccepted);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new GetMobileFlightsQueryHandler(db, Scope(), NoopCustomerLogoReader.Instance, new SchedulingMasterDataReader())
            .Handle(new GetMobileFlightsQuery(), CancellationToken.None);

        var row = result.Value.Items.ShouldHaveSingleItem();
        row.HasCancellationSubmission.ShouldBeTrue();
        row.LatestCancellationAtUtc.ShouldBe(Now.AddMinutes(-10));
        row.ActiveWorkCount.ShouldBe(0);
        row.CompletedWorkCount.ShouldBe(0);
        row.LatestReturnToRampSequence.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(MobileFlightCategory.MyFlights)]
    [InlineData(MobileFlightCategory.PerLanding)]
    [InlineData(MobileFlightCategory.AdHoc)]
    public void MobileCardProjection_TranslatesWithTheProductionSqlServerProvider(MobileFlightCategory? category)
    {
        using var db = new OperationsDbContext(new DbContextOptionsBuilder<OperationsDbContext>()
            .UseSqlServer("Server=localhost;Database=translation-only;Integrated Security=true;TrustServerCertificate=true").Options);

        var sql = MobileFlightQuery.ProjectListItems(
                MobileFlightQuery.ApplyScope(db.Flights.AsNoTracking(), Context, category)
                    .OrderBy(f => f.Id).Skip(0).Take(20), Context, "Asia/Riyadh")
            .ToQueryString();

        sql.ShouldContain("WHERE");
        sql.ShouldContain("StationId");
        sql.ShouldContain("ScheduledArrivalUtc");
        sql.ShouldContain("ScheduledDepartureUtc");
        sql.ShouldContain("flight_services");
        sql.ShouldContain("flight_tasks");
        sql.ShouldContain("flight_return_to_ramps");
        sql.ShouldContain("CanceledAtUtc");
    }

    private static StaticMobileScope Scope() => new(Context);

    private static OperationsDbContext NewDb() => new(new DbContextOptionsBuilder<OperationsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Flight FlightFor(string number, bool assigned = false, bool perLanding = false,
        bool adHoc = false, Guid? stationId = null, Guid? createdBy = null, DateTimeOffset? sta = null, DateTimeOffset? std = null) =>
        Flight.ScheduleNew(new CustomerSnapshot(Guid.NewGuid(), "SV", "Saudia"),
            new StationSnapshot(stationId ?? StationId, "RUH", "Riyadh"),
            new OperationTypeSnapshot(adHoc ? WellKnownMasterDataIds.AdHocOperationType : Guid.NewGuid(), adHoc ? "Ad Hoc" : "Transit"),
            FlightNumber.Create($"SV{number}", "SV").Value, ScheduledTime.Create(sta ?? Now, std ?? Now.AddHours(1)).Value,
            null, [new ServiceSnapshot(perLanding ? WellKnownMasterDataIds.AircraftPerLandingService : Guid.NewGuid(), "Service")],
            assigned && !perLanding ? [new StaffMemberSnapshot(StaffId, "Assigned staff", "E1")] : [], createdBy ?? UserId, Now).Value;

    private sealed class StaticMobileScope(MobileFlightScopeContext context) : IMobileFlightScope
    {
        public Task<Result<MobileFlightScopeContext>> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(context));
    }

    private sealed class Actor(UserType userType = UserType.StationStaff) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => MobileFlightTests.UserId;
        public UserType? UserType => userType;
        public Guid? ExternalReferenceId => StaffId;
        public bool HasPermission(string permission) => true;
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class AuditActor : IAuditContext
    {
        public Guid? ActorId => UserId;
        public string? ActorDisplayName => "Staff";
        public bool IsSystemActor => false;
        public string? CorrelationId => null;
    }

    private sealed class LogoReader : ICustomerLogoReader
    {
        public int ContentCalls { get; private set; }
        public List<Guid[]> VersionBatches { get; } = [];

        public Task<CustomerLogoReadContent?> GetLogoAsync(Guid customerId, CancellationToken cancellationToken)
        {
            ContentCalls++;
            return Task.FromResult<CustomerLogoReadContent?>(new([137, 80, 78, 71], "image/png", "logo-v1"));
        }

        public Task<IReadOnlyDictionary<Guid, string>> GetLogoVersionsAsync(IReadOnlyCollection<Guid> customerIds, CancellationToken cancellationToken)
        {
            VersionBatches.Add(customerIds.ToArray());
            return Task.FromResult<IReadOnlyDictionary<Guid, string>>(customerIds.ToDictionary(id => id, _ => "logo-v1"));
        }
    }
}
