using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Authorization;
using Operations.Application.Features.Flights;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class FlightQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("RJ-1")]
    [InlineData("RJ1")]
    public async Task GetFlights_SearchMatchesDisplayedCustomerIataFlightNumber(string search)
    {
        await using var db = NewDb();
        var flight = CreateScheduledFlight(customerIata: "RJ", customerName: "Royal Jordanian", flightNumber: "1");
        db.Flights.Add(flight);
        db.Flights.Add(CreateScheduledFlight(customerIata: "SV", customerName: "Saudia", flightNumber: "2"));
        await db.SaveChangesAsync();

        var handler = new GetFlightsQueryHandler(
            db,
            new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null)));

        var result = await handler.Handle(new GetFlightsQuery(Search: search), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Select(f => f.Id).ShouldBe([flight.Id]);
    }

    [Fact]
    public async Task GetSchedulerCalendar_FiltersByStatusStationAndCustomer()
    {
        await using var db = NewDb();
        var stationId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var flight = CreateScheduledFlight(
            customerIata: "RJ",
            customerName: "Royal Jordanian",
            flightNumber: "1",
            customerId: customerId,
            stationId: stationId);

        db.Flights.Add(flight);
        db.Flights.Add(CreateScheduledFlight(customerIata: "SV", customerName: "Saudia", flightNumber: "2", stationId: stationId));
        db.Flights.Add(CreateScheduledFlight(customerIata: "RJ", customerName: "Royal Jordanian", flightNumber: "3", customerId: customerId));
        await db.SaveChangesAsync();

        var handler = new GetSchedulerCalendarQueryHandler(
            db,
            new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null)));

        var result = await handler.Handle(new GetSchedulerCalendarQuery(
            Now.AddHours(-1),
            Now.AddHours(2),
            stationId,
            customerId,
            FlightStatus.Scheduled), CancellationToken.None);
        var canceledResult = await handler.Handle(new GetSchedulerCalendarQuery(
            Now.AddHours(-1),
            Now.AddHours(2),
            stationId,
            customerId,
            FlightStatus.Canceled), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(f => f.Id).ShouldBe([flight.Id]);
        result.Value.Single().CustomerIataCode.ShouldBe("RJ");
        result.Value.Single().StationIata.ShouldBe("DMM");
        result.Value.Single().StationName.ShouldBe("Dammam");
        canceledResult.IsSuccess.ShouldBeTrue();
        canceledResult.Value.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("D")]
    [InlineData("N")]
    public async Task GetFlights_SearchMatchesFlightId(string format)
    {
        await using var db = NewDb();
        var flight = CreateScheduledFlight(customerIata: "RJ", customerName: "Royal Jordanian", flightNumber: "1");
        db.Flights.Add(flight);
        db.Flights.Add(CreateScheduledFlight(customerIata: "SV", customerName: "Saudia", flightNumber: "2"));
        await db.SaveChangesAsync();

        var handler = new GetFlightsQueryHandler(
            db,
            new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null)));

        var result = await handler.Handle(new GetFlightsQuery(Search: flight.Id.ToString(format)), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Select(f => f.Id).ShouldBe([flight.Id]);
    }

    [Fact]
    public async Task GetFlightsExport_ReturnsEveryMatchingRowBeyondPagedListLimit()
    {
        await using var db = NewDb();
        var flights = Enumerable.Range(1, 125)
            .Select(number => CreateScheduledFlight(
                customerIata: "RJ",
                customerName: "Royal Jordanian",
                flightNumber: number.ToString()))
            .ToList();
        db.Flights.AddRange(flights);
        await db.SaveChangesAsync();

        var scope = new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null));
        var listResult = await new GetFlightsQueryHandler(db, scope).Handle(
            new GetFlightsQuery(Page: 1, PageSize: int.MaxValue),
            CancellationToken.None);
        var exportResult = await new GetFlightsExportQueryHandler(db, scope).Handle(
            new GetFlightsExportQuery(),
            CancellationToken.None);

        listResult.IsSuccess.ShouldBeTrue();
        listResult.Value.Items.Count.ShouldBe(100);
        listResult.Value.TotalCount.ShouldBe(125);
        exportResult.IsSuccess.ShouldBeTrue();
        exportResult.Value.Count.ShouldBe(125);
    }

    [Fact]
    public async Task GetFlightsExport_AppliesListFiltersAndAssignedStaffScope()
    {
        await using var db = NewDb();
        var stationId = Guid.NewGuid();
        var otherStationId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var operationTypeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        StaffMemberSnapshot[] assignedStaff = [new(staffId, "Scoped Staff", "EMP-100")];

        var matching = CreateScheduledFlight(
            "RJ", "Royal Jordanian", "MATCH100", customerId, stationId, operationTypeId, Now, assignedStaff);
        var unassignedAtOwnStation = CreateScheduledFlight(
            "RJ", "Royal Jordanian", "MATCH200", customerId, stationId, operationTypeId, Now);
        var assignedAtOtherStation = CreateScheduledFlight(
            "RJ", "Royal Jordanian", "MATCH300", customerId, otherStationId, operationTypeId, Now, assignedStaff);
        var wrongCustomer = CreateScheduledFlight(
            "SV", "Saudia", "MATCH400", otherCustomerId, stationId, operationTypeId, Now, assignedStaff);
        var outsideDateRange = CreateScheduledFlight(
            "RJ", "Royal Jordanian", "MATCH500", customerId, stationId, operationTypeId, Now.AddDays(-2), assignedStaff);
        var wrongStatus = CreateScheduledFlight(
            "RJ", "Royal Jordanian", "MATCH600", customerId, stationId, operationTypeId, Now, assignedStaff);
        wrongStatus.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        wrongStatus.SettleCompleted(Now).IsSuccess.ShouldBeTrue();

        db.Flights.AddRange(
            matching,
            unassignedAtOwnStation,
            assignedAtOtherStation,
            wrongCustomer,
            outsideDateRange,
            wrongStatus);
        await db.SaveChangesAsync();

        var scope = new StaticScope(new OperationsScopeContext(UserType.StationStaff, stationId, staffId));
        var result = await new GetFlightsExportQueryHandler(db, scope).Handle(
            new GetFlightsExportQuery(
                Search: "match",
                CustomerId: customerId,
                OperationTypeId: operationTypeId,
                Statuses: [FlightStatus.Scheduled],
                FromUtc: Now.AddHours(-1),
                ToUtc: Now.AddHours(1)),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(f => f.Id).ShouldBe([matching.Id]);
        result.Value.Single().StationName.ShouldBe("Dammam");
    }

    [Fact]
    public async Task GetFlightsExport_UsesOnlyApprovedWorkOrderValues()
    {
        await using var db = NewDb();
        var approvedFlight = CreateScheduledFlight("RJ", "Royal Jordanian", "100");
        var submittedFlight = CreateScheduledFlight("SV", "Saudia", "200");
        db.Flights.AddRange(approvedFlight, submittedFlight);
        await db.SaveChangesAsync();

        var approved = CreateWorkOrder(approvedFlight, "101", "Approved remarks");
        approved.Approve(1, "DMM-0001", Guid.NewGuid(), Now.AddMinutes(5)).IsSuccess.ShouldBeTrue();
        var submitted = CreateWorkOrder(submittedFlight, "201", "Submitted remarks");
        db.WorkOrders.AddRange(approved, submitted);
        await db.SaveChangesAsync();

        var scope = new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null));
        var result = await new GetFlightsExportQueryHandler(db, scope).Handle(
            new GetFlightsExportQuery(Sort: "FlightNumber:asc"),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var approvedRow = result.Value.Single(row => row.Id == approvedFlight.Id);
        approvedRow.ApprovedWorkOrder.ShouldNotBeNull();
        approvedRow.ApprovedWorkOrder!.ApprovalNumber.ShouldBe("DMM-0001");
        approvedRow.ApprovedWorkOrder.ActualFlightNumber.ShouldBe("101");
        approvedRow.ApprovedWorkOrder.AircraftManufacturer.ShouldBe("Airbus");
        approvedRow.ApprovedWorkOrder.AircraftModel.ShouldBe("A320");
        approvedRow.ApprovedWorkOrder.AircraftTailNumber.ShouldBe("HZ-ABC");
        approvedRow.ApprovedWorkOrder.ServiceNames.ShouldBe(["Deicing"]);
        approvedRow.ApprovedWorkOrder.Remarks.ShouldBe("Approved remarks");
        result.Value.Single(row => row.Id == submittedFlight.Id).ApprovedWorkOrder.ShouldBeNull();
    }

    [Fact]
    public async Task GetFlights_AcceptsMultipleStatuses()
    {
        await using var db = NewDb();
        var scheduled = CreateScheduledFlight("RJ", "Royal Jordanian", "100");
        var completed = CreateScheduledFlight("SV", "Saudia", "200");
        completed.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        completed.SettleCompleted(Now).IsSuccess.ShouldBeTrue();
        var canceled = CreateScheduledFlight("XY", "Other", "300");
        canceled.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        canceled.SettleCanceled(Now).IsSuccess.ShouldBeTrue();
        db.Flights.AddRange(scheduled, completed, canceled);
        await db.SaveChangesAsync();

        var scope = new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null));
        var result = await new GetFlightsQueryHandler(db, scope).Handle(
            new GetFlightsQuery(PageSize: 100, Statuses: [FlightStatus.Scheduled, FlightStatus.Completed]),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Select(row => row.Id).ShouldBe([scheduled.Id, completed.Id], ignoreOrder: true);
    }

    [Theory]
    [InlineData(FlightServiceCategory.PerLanding)]
    [InlineData(FlightServiceCategory.OnCall)]
    [InlineData(FlightServiceCategory.Other)]
    public async Task GetFlights_FiltersByServiceCategory(FlightServiceCategory category)
    {
        await using var db = NewDb();
        var perLanding = CreateScheduledFlight(
            "PL", "Per Landing", "100", plannedServices: [PerLandingService()]);
        var onCall = CreateScheduledFlight("OC", "On Call", "200");
        var other = CreateScheduledFlight("OT", "Other", "300");
        db.Flights.AddRange(perLanding, onCall, other);
        await db.SaveChangesAsync();

        db.WorkOrders.Add(CreateWorkOrder(
            onCall,
            "200",
            "On Call",
            new ServiceSnapshot(WellKnownMasterDataIds.OnCallService, "On Call")));
        await db.SaveChangesAsync();

        var scope = new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null));
        var result = await new GetFlightsQueryHandler(db, scope).Handle(
            new GetFlightsQuery(PageSize: 100, ServiceCategories: [category]),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var expectedId = category switch
        {
            FlightServiceCategory.PerLanding => perLanding.Id,
            FlightServiceCategory.OnCall => onCall.Id,
            _ => other.Id
        };
        result.Value.Items.Select(row => row.Id).ShouldBe([expectedId]);
    }

    [Fact]
    public async Task GetPerLandingExtraction_ReturnsOnlyInProgressPerLandingWithoutOnCall()
    {
        await using var db = NewDb();
        var eligible = CreateScheduledFlight(
            "PL", "Eligible", "100", plannedServices: [PerLandingService()]);
        var onCall = CreateScheduledFlight(
            "PL", "On Call", "200", plannedServices: [PerLandingService()]);
        var scheduled = CreateScheduledFlight(
            "PL", "Scheduled", "300", plannedServices: [PerLandingService()]);
        eligible.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        onCall.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        db.Flights.AddRange(eligible, onCall, scheduled);
        await db.SaveChangesAsync();

        var eligibleWorkOrder = CreateWorkOrder(eligible, "100", "Per Landing review");
        var onCallWorkOrder = CreateWorkOrder(
            onCall,
            "200",
            "On Call",
            new ServiceSnapshot(WellKnownMasterDataIds.OnCallService, "On Call"));
        db.WorkOrders.AddRange(eligibleWorkOrder, onCallWorkOrder);
        await db.SaveChangesAsync();

        var scope = new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null));
        var result = await new GetPerLandingExtractionQueryHandler(db, scope).Handle(
            new GetPerLandingExtractionQuery(),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var item = result.Value.ShouldHaveSingleItem();
        item.FlightId.ShouldBe(eligible.Id);
        item.WorkOrderId.ShouldBe(eligibleWorkOrder.Id);
    }

    [Theory]
    [InlineData("FlightNumber:asc")]
    [InlineData("FlightNumber:desc")]
    [InlineData("unsupported:asc")]
    public async Task GetFlightsAndExport_UseTheSameWhitelistedSort(string sort)
    {
        await using var db = NewDb();
        db.Flights.AddRange(
            CreateScheduledFlight("RJ", "Royal Jordanian", "300"),
            CreateScheduledFlight("RJ", "Royal Jordanian", "100"),
            CreateScheduledFlight("RJ", "Royal Jordanian", "200"));
        await db.SaveChangesAsync();

        var scope = new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null));
        var listResult = await new GetFlightsQueryHandler(db, scope).Handle(
            new GetFlightsQuery(PageSize: 100, Sort: sort),
            CancellationToken.None);
        var exportResult = await new GetFlightsExportQueryHandler(db, scope).Handle(
            new GetFlightsExportQuery(Sort: sort),
            CancellationToken.None);

        listResult.IsSuccess.ShouldBeTrue();
        exportResult.IsSuccess.ShouldBeTrue();
        exportResult.Value.Select(f => f.Id).ShouldBe(listResult.Value.Items.Select(f => f.Id));

        if (sort == "FlightNumber:asc")
            exportResult.Value.Select(f => f.FlightNumber).ShouldBe(["100", "200", "300"]);
        else if (sort == "FlightNumber:desc")
            exportResult.Value.Select(f => f.FlightNumber).ShouldBe(["300", "200", "100"]);
    }

    private static OperationsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OperationsDbContext>()
            .UseInMemoryDatabase($"ops-{Guid.NewGuid()}")
            .Options);

    private static Flight CreateScheduledFlight(
        string customerIata,
        string customerName,
        string flightNumber,
        Guid? customerId = null,
        Guid? stationId = null,
        Guid? operationTypeId = null,
        DateTimeOffset? scheduledArrival = null,
        IReadOnlyList<StaffMemberSnapshot>? assignedEmployees = null,
        IReadOnlyList<ServiceSnapshot>? plannedServices = null)
    {
        var arrival = scheduledArrival ?? Now;
        return Flight.ScheduleNew(
            new CustomerSnapshot(customerId ?? Guid.NewGuid(), customerIata, customerName),
            new StationSnapshot(stationId ?? Guid.NewGuid(), "DMM", "Dammam"),
            new OperationTypeSnapshot(operationTypeId ?? Guid.NewGuid(), "Transit"),
            FlightNumber.Create(flightNumber).Value,
            ScheduledTime.Create(arrival, arrival.AddHours(1)).Value,
            aircraftType: null,
            plannedServices: plannedServices ?? [new ServiceSnapshot(Guid.NewGuid(), "Marshalling")],
            assignedEmployees: assignedEmployees?
                .Select(employee => new StaffMemberSnapshot(employee.StaffMemberId, employee.FullName, employee.EmployeeId))
                .ToList() ?? [],
            contractId: null,
            contractNumber: null,
            createdByUserId: Guid.NewGuid(),
            now: Now).Value;
    }

    private static WorkOrder CreateWorkOrder(
        Flight flight,
        string actualFlightNumber,
        string remarks,
        ServiceSnapshot? service = null)
    {
        var employee = new StaffMemberSnapshot(Guid.NewGuid(), "Report Engineer", "ENG-1");
        var workOrder = WorkOrder.SubmitNew(
            flight,
            WorkOrderType.Completion,
            Guid.NewGuid(),
            employee,
            FlightNumber.Create(actualFlightNumber).Value,
            new AircraftTypeSnapshot(Guid.NewGuid(), "Airbus", "A320"),
            "HZ-ABC",
            ActualTime.Create(Now.AddMinutes(10), Now.AddHours(1).AddMinutes(15)).Value,
            null,
            remarks,
            [new WorkOrderServiceLineInput(
                service ?? new ServiceSnapshot(Guid.NewGuid(), "Deicing"),
                employee,
                TimeWindow.Create(Now.AddMinutes(10), Now.AddMinutes(35)).Value,
                null)],
            [],
            Now).Value;
        return workOrder;
    }

    private static ServiceSnapshot PerLandingService() =>
        new(WellKnownMasterDataIds.AircraftPerLandingService, "Aircraft Per Landing");

    private sealed class StaticScope(OperationsScopeContext context) : IOperationsScope
    {
        public Task<Result<OperationsScopeContext>> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(context));
    }
}
