using BuildingBlocks.Application.Abstractions;
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
    public async Task GetFlightsExport_CompletedFlightUsesApprovedWorkOrderValues()
    {
        await using var db = NewDb();
        var completedFlight = CreateScheduledFlight("RJ", "Royal Jordanian", "100");

        var approved = CreateWorkOrder(completedFlight, "101", "Approved remarks");
        var laterSubmitted = CreateWorkOrder(
            completedFlight,
            "102",
            "Later submitted remarks",
            submittedAt: Now.AddMinutes(1));
        approved.Approve(1, "DMM-0001", Guid.NewGuid(), Now.AddMinutes(5)).IsSuccess.ShouldBeTrue();
        completedFlight.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        completedFlight.SettleCompleted(Now.AddMinutes(5)).IsSuccess.ShouldBeTrue();

        db.Flights.Add(completedFlight);
        db.WorkOrders.AddRange(approved, laterSubmitted);
        await db.SaveChangesAsync();

        var scope = new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null));
        var result = await new GetFlightsExportQueryHandler(db, scope).Handle(
            new GetFlightsExportQuery(Sort: "FlightNumber:asc"),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var approvedRow = result.Value.ShouldHaveSingleItem();
        approvedRow.ApprovedWorkOrder.ShouldNotBeNull();
        approvedRow.ApprovedWorkOrder!.ApprovalNumber.ShouldBe("DMM-0001");
        approvedRow.ApprovedWorkOrder.ActualFlightNumber.ShouldBe("101");
        approvedRow.ApprovedWorkOrder.AircraftManufacturer.ShouldBe("Airbus");
        approvedRow.ApprovedWorkOrder.AircraftModel.ShouldBe("A320");
        approvedRow.ApprovedWorkOrder.AircraftTailNumber.ShouldBe("HZ-ABC");
        approvedRow.ApprovedWorkOrder.ServiceNames.ShouldBe(["Deicing"]);
        approvedRow.ApprovedWorkOrder.Remarks.ShouldBe("Approved remarks");
    }

    [Fact]
    public async Task GetFlightsExport_InProgressFlightUsesNewestSubmittedWorkOrderWithDeterministicTieBreak()
    {
        await using var db = NewDb();
        var flight = CreateScheduledFlight("RJ", "Royal Jordanian", "200");
        flight.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();

        var older = CreateWorkOrder(
            flight,
            "201",
            "Older submitted remarks",
            submittedAt: Now);
        var lowerIdAtNewestTime = CreateWorkOrder(
            flight,
            "202",
            "Lower id submitted remarks",
            submittedAt: Now.AddMinutes(1),
            id: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var higherIdAtNewestTime = CreateWorkOrder(
            flight,
            "203",
            "Higher id submitted remarks",
            submittedAt: Now.AddMinutes(1),
            id: Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var newerApproved = CreateWorkOrder(
            flight,
            "204",
            "Approved values must not leak into an in-progress row",
            submittedAt: Now.AddMinutes(2));
        newerApproved.Approve(1, "DMM-0002", Guid.NewGuid(), Now.AddMinutes(3)).IsSuccess.ShouldBeTrue();

        db.Flights.Add(flight);
        db.WorkOrders.AddRange(older, lowerIdAtNewestTime, higherIdAtNewestTime, newerApproved);
        await db.SaveChangesAsync();

        var scope = new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null));
        var result = await new GetFlightsExportQueryHandler(db, scope).Handle(
            new GetFlightsExportQuery(),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var exportedWorkOrder = result.Value.ShouldHaveSingleItem().ApprovedWorkOrder;
        exportedWorkOrder.ShouldNotBeNull();
        exportedWorkOrder!.ActualFlightNumber.ShouldBe("203");
        exportedWorkOrder.Remarks.ShouldBe("Higher id submitted remarks");
        exportedWorkOrder.ApprovalNumber.ShouldBeNull();
    }

    [Fact]
    public async Task GetFlightsExport_InProgressFlightIncludesReturnedWorkOrderValues()
    {
        await using var db = NewDb();
        var flight = CreateScheduledFlight("RJ", "Royal Jordanian", "300");
        var returned = CreateWorkOrder(flight, "301", "Returned remarks");
        flight.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        returned.Approve(1, "DMM-0003", Guid.NewGuid(), Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        flight.SettleCompleted(Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        returned.Return(Guid.NewGuid(), "Correct the work order", Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        flight.ReopenToInProgress(Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();

        db.Flights.Add(flight);
        db.WorkOrders.Add(returned);
        await db.SaveChangesAsync();

        var scope = new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null));
        var result = await new GetFlightsExportQueryHandler(db, scope).Handle(
            new GetFlightsExportQuery(),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var exportedWorkOrder = result.Value.ShouldHaveSingleItem().ApprovedWorkOrder;
        exportedWorkOrder.ShouldNotBeNull();
        exportedWorkOrder!.ActualFlightNumber.ShouldBe("301");
        exportedWorkOrder.Remarks.ShouldBe("Returned remarks");
        exportedWorkOrder.ApprovalNumber.ShouldBeNull();
    }

    [Fact]
    public async Task GetFlightsExport_ScheduledFlightKeepsWorkOrderValuesEmpty()
    {
        await using var db = NewDb();
        var flight = CreateScheduledFlight("RJ", "Royal Jordanian", "400");
        var submitted = CreateWorkOrder(flight, "401", "Submitted remarks");
        var approved = CreateWorkOrder(
            flight,
            "402",
            "Approved values must not leak into a scheduled row",
            submittedAt: Now.AddMinutes(1));
        approved.Approve(1, "DMM-0004", Guid.NewGuid(), Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();

        db.Flights.Add(flight);
        db.WorkOrders.AddRange(submitted, approved);
        await db.SaveChangesAsync();

        var scope = new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null));
        var result = await new GetFlightsExportQueryHandler(db, scope).Handle(
            new GetFlightsExportQuery(),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldHaveSingleItem().ApprovedWorkOrder.ShouldBeNull();
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
    public async Task GetFlightsAndExport_UseMutuallyExclusiveServiceCategories(FlightServiceCategory category)
    {
        await using var db = NewDb();
        var perLanding = CreateScheduledFlight(
            "PL", "Per Landing", "100", plannedServices: [PerLandingService()]);
        var onCall = CreateScheduledFlight(
            "PL", "On Call", "200", plannedServices: [PerLandingService()]);
        var other = CreateScheduledFlight("OT", "Other", "300");
        db.Flights.AddRange(perLanding, onCall, other);
        await db.SaveChangesAsync();

        db.WorkOrders.AddRange(
            CreateWorkOrder(onCall, "200", "Performed service"),
            CreateWorkOrder(other, "300", "Non-Per-Landing performed service"));
        await db.SaveChangesAsync();

        var scope = new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null));
        var listResult = await new GetFlightsQueryHandler(db, scope).Handle(
            new GetFlightsQuery(PageSize: 100, ServiceCategories: [category]),
            CancellationToken.None);
        var exportResult = await new GetFlightsExportQueryHandler(db, scope).Handle(
            new GetFlightsExportQuery(ServiceCategories: [category]),
            CancellationToken.None);

        listResult.IsSuccess.ShouldBeTrue();
        exportResult.IsSuccess.ShouldBeTrue();
        var expectedId = category switch
        {
            FlightServiceCategory.PerLanding => perLanding.Id,
            FlightServiceCategory.OnCall => onCall.Id,
            _ => other.Id
        };
        listResult.Value.Items.Select(row => row.Id).ShouldBe([expectedId]);
        exportResult.Value.Select(row => row.Id).ShouldBe([expectedId]);

        var item = listResult.Value.Items.Single();
        item.IsPerLanding.ShouldBe(category is not FlightServiceCategory.Other);
        item.IsOnCall.ShouldBe(category is FlightServiceCategory.OnCall);
    }

    [Fact]
    public async Task OnCall_IsDerivedConsistentlyForListCalendarAndDetail()
    {
        await using var db = NewDb();
        var onCall = CreateScheduledFlight(
            "PL", "On Call", "100", plannedServices: [PerLandingService()]);
        var other = CreateScheduledFlight("OT", "Other", "200");
        db.Flights.AddRange(onCall, other);
        await db.SaveChangesAsync();

        db.WorkOrders.AddRange(
            CreateWorkOrder(onCall, "100", "Arbitrary performed service"),
            CreateWorkOrder(other, "200", "Arbitrary performed service"));
        await db.SaveChangesAsync();

        var scope = new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null));
        var list = await new GetFlightsQueryHandler(db, scope).Handle(
            new GetFlightsQuery(PageSize: 100),
            CancellationToken.None);
        var calendar = await new GetSchedulerCalendarQueryHandler(db, scope).Handle(
            new GetSchedulerCalendarQuery(Now.AddMinutes(-1), Now.AddMinutes(1)),
            CancellationToken.None);
        var onCallDetail = await new GetFlightByIdQueryHandler(db, scope, new StaticUserContext()).Handle(
            new GetFlightByIdQuery(onCall.Id),
            CancellationToken.None);
        var otherDetail = await new GetFlightByIdQueryHandler(db, scope, new StaticUserContext()).Handle(
            new GetFlightByIdQuery(other.Id),
            CancellationToken.None);

        list.IsSuccess.ShouldBeTrue();
        list.Value.Items.Single(row => row.Id == onCall.Id).IsOnCall.ShouldBeTrue();
        list.Value.Items.Single(row => row.Id == other.Id).IsOnCall.ShouldBeFalse();
        calendar.IsSuccess.ShouldBeTrue();
        calendar.Value.Single(row => row.Id == onCall.Id).IsOnCall.ShouldBeTrue();
        calendar.Value.Single(row => row.Id == other.Id).IsOnCall.ShouldBeFalse();
        onCallDetail.IsSuccess.ShouldBeTrue();
        onCallDetail.Value.IsOnCall.ShouldBeTrue();
        otherDetail.IsSuccess.ShouldBeTrue();
        otherDetail.Value.IsOnCall.ShouldBeFalse();
    }

    [Theory]
    [InlineData(WorkOrderStatus.Submitted, WorkOrderType.Completion, false, true)]
    [InlineData(WorkOrderStatus.Returned, WorkOrderType.Completion, false, true)]
    [InlineData(WorkOrderStatus.Approved, WorkOrderType.Completion, false, true)]
    [InlineData(WorkOrderStatus.Submitted, WorkOrderType.Cancellation, false, true)]
    [InlineData(WorkOrderStatus.Submitted, WorkOrderType.Completion, true, true)]
    [InlineData(WorkOrderStatus.Merged, WorkOrderType.Completion, false, false)]
    public async Task GetFlights_OnCallCountsEveryNonMergedStatusTypeAndMergeGeneratedTarget(
        WorkOrderStatus status,
        WorkOrderType type,
        bool mergeGenerated,
        bool expectedOnCall)
    {
        await using var db = NewDb();
        var flight = CreateScheduledFlight(
            "PL", "Per Landing", "100", plannedServices: [PerLandingService()]);
        var workOrder = CreateWorkOrder(
            flight,
            "100",
            "Performed service",
            type: type,
            mergeGenerated: mergeGenerated);
        TransitionWorkOrder(workOrder, status);
        db.Flights.Add(flight);
        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync();

        var scope = new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null));
        var result = await new GetFlightsQueryHandler(db, scope).Handle(
            new GetFlightsQuery(),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.ShouldHaveSingleItem().IsOnCall.ShouldBe(expectedOnCall);
    }

    [Fact]
    public async Task GetFlights_EmptyAndTasksOnlyWorkOrdersDoNotSetOnCall()
    {
        await using var db = NewDb();
        var empty = CreateScheduledFlight(
            "PL", "Empty", "100", plannedServices: [PerLandingService()]);
        var tasksOnly = CreateScheduledFlight(
            "PL", "Tasks only", "200", plannedServices: [PerLandingService()]);
        db.Flights.AddRange(empty, tasksOnly);
        db.WorkOrders.AddRange(
            CreateWorkOrder(empty, "100", "Empty", includeService: false),
            CreateWorkOrder(
                tasksOnly,
                "200",
                "Tasks only",
                includeService: false,
                tasks: [TaskInput()]));
        await db.SaveChangesAsync();

        var scope = new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null));
        var result = await new GetFlightsQueryHandler(db, scope).Handle(
            new GetFlightsQuery(PageSize: 100),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.ShouldAllBe(row => !row.IsOnCall);
    }

    [Fact]
    public async Task GetFlights_RemovingLastServiceLineAndDeletingWorkOrderClearOnCall()
    {
        await using var db = NewDb();
        var flight = CreateScheduledFlight(
            "PL", "Per Landing", "100", plannedServices: [PerLandingService()]);
        var workOrder = CreateWorkOrder(flight, "100", "Performed service");
        db.Flights.Add(flight);
        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync();

        var scope = new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null));
        var handler = new GetFlightsQueryHandler(db, scope);
        (await handler.Handle(new GetFlightsQuery(), CancellationToken.None))
            .Value.Items.ShouldHaveSingleItem().IsOnCall.ShouldBeTrue();

        workOrder.UpdateDetails(
            workOrder.Type,
            workOrder.ActualFlightNumber,
            workOrder.AircraftType,
            workOrder.AircraftTailNumber,
            workOrder.Actuals,
            workOrder.Cancellation,
            workOrder.Remarks,
            serviceLines: [],
            tasks: [],
            Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();

        (await handler.Handle(new GetFlightsQuery(), CancellationToken.None))
            .Value.Items.ShouldHaveSingleItem().IsOnCall.ShouldBeFalse();

        workOrder.UpdateDetails(
            workOrder.Type,
            workOrder.ActualFlightNumber,
            workOrder.AircraftType,
            workOrder.AircraftTailNumber,
            workOrder.Actuals,
            workOrder.Cancellation,
            workOrder.Remarks,
            serviceLines: [ServiceLineInput()],
            tasks: [],
            Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        (await handler.Handle(new GetFlightsQuery(), CancellationToken.None))
            .Value.Items.ShouldHaveSingleItem().IsOnCall.ShouldBeTrue();

        db.WorkOrders.Remove(workOrder);
        await db.SaveChangesAsync();

        (await handler.Handle(new GetFlightsQuery(), CancellationToken.None))
            .Value.Items.ShouldHaveSingleItem().IsOnCall.ShouldBeFalse();
    }

    [Fact]
    public async Task GetPerLandingExtraction_ReturnsOnlyInProgressPerLandingWithoutPerformedServices()
    {
        await using var db = NewDb();
        var eligible = CreateScheduledFlight(
            "PL", "Eligible", "100", plannedServices: [PerLandingService()]);
        var tasksOnly = CreateScheduledFlight(
            "PL", "Tasks only", "200", plannedServices: [PerLandingService()]);
        var performed = CreateScheduledFlight(
            "PL", "Performed", "300", plannedServices: [PerLandingService()]);
        var cancellationPerformed = CreateScheduledFlight(
            "PL", "Cancellation performed", "400", plannedServices: [PerLandingService()]);
        var mergedSource = CreateScheduledFlight(
            "PL", "Merged source", "500", plannedServices: [PerLandingService()]);
        var scheduled = CreateScheduledFlight(
            "PL", "Scheduled", "600", plannedServices: [PerLandingService()]);
        eligible.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        tasksOnly.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        performed.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        cancellationPerformed.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        mergedSource.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        db.Flights.AddRange(eligible, tasksOnly, performed, cancellationPerformed, mergedSource, scheduled);
        await db.SaveChangesAsync();

        var eligibleWorkOrder = CreateWorkOrder(eligible, "100", "Per Landing review", includeService: false);
        var tasksOnlyWorkOrder = CreateWorkOrder(
            tasksOnly,
            "200",
            "Tasks only",
            includeService: false,
            tasks: [TaskInput()]);
        var performedReview = CreateWorkOrder(performed, "300", "Review", includeService: false);
        var performedWorkOrder = CreateWorkOrder(performed, "300", "Performed service");
        var cancellationReview = CreateWorkOrder(cancellationPerformed, "400", "Review", includeService: false);
        var cancellationWorkOrder = CreateWorkOrder(
            cancellationPerformed,
            "400",
            "Cancellation performed service",
            type: WorkOrderType.Cancellation);
        var mergedGenerated = CreateWorkOrder(
            mergedSource,
            "500",
            "Merged review",
            includeService: false,
            mergeGenerated: true);
        var mergedServiceSource = CreateWorkOrder(mergedSource, "500", "Merged performed service");
        mergedServiceSource.MarkMergedInto(mergedGenerated.Id, Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        db.WorkOrders.AddRange(
            eligibleWorkOrder,
            tasksOnlyWorkOrder,
            performedReview,
            performedWorkOrder,
            cancellationReview,
            cancellationWorkOrder,
            mergedGenerated,
            mergedServiceSource);
        await db.SaveChangesAsync();

        var scope = new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null));
        var result = await new GetPerLandingExtractionQueryHandler(db, scope).Handle(
            new GetPerLandingExtractionQuery(),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(item => item.FlightId).ShouldBe(
            [eligible.Id, tasksOnly.Id, mergedSource.Id],
            ignoreOrder: true);
        result.Value.Single(item => item.FlightId == eligible.Id).WorkOrderId.ShouldBe(eligibleWorkOrder.Id);
        result.Value.Single(item => item.FlightId == tasksOnly.Id).WorkOrderId.ShouldBe(tasksOnlyWorkOrder.Id);
        result.Value.Single(item => item.FlightId == mergedSource.Id).WorkOrderId.ShouldBe(mergedGenerated.Id);
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
        bool includeService = true,
        WorkOrderType type = WorkOrderType.Completion,
        IReadOnlyList<WorkOrderTaskInput>? tasks = null,
        bool mergeGenerated = false,
        DateTimeOffset? submittedAt = null,
        Guid? id = null)
    {
        var now = submittedAt ?? Now;
        var employee = new StaffMemberSnapshot(Guid.NewGuid(), "Report Engineer", "ENG-1");
        WorkOrderServiceLineInput[] serviceLines = includeService
            ? [ServiceLineInput(employee)]
            : [];
        var cancellation = type == WorkOrderType.Cancellation
            ? CancellationDetails.Create(Now.AddMinutes(5), "Customer canceled").Value
            : null;

        var ownerUserId = Guid.NewGuid();
        var actualNumber = FlightNumber.Create(actualFlightNumber).Value;
        var aircraftType = new AircraftTypeSnapshot(Guid.NewGuid(), "Airbus", "A320");
        var actuals = ActualTime.Create(Now.AddMinutes(10), Now.AddHours(1).AddMinutes(15)).Value;
        var workOrder = mergeGenerated
            ? WorkOrder.SubmitMerged(
                flight, type, ownerUserId, employee, actualNumber, aircraftType, "HZ-ABC", actuals,
                cancellation, remarks, serviceLines, tasks ?? [], now, id)
            : WorkOrder.SubmitNew(
                flight, type, ownerUserId, employee, actualNumber, aircraftType, "HZ-ABC", actuals,
                cancellation, remarks, serviceLines, tasks ?? [], now, id);

        return workOrder.Value;
    }

    private static WorkOrderServiceLineInput ServiceLineInput(StaffMemberSnapshot? employee = null) =>
        new(
            new ServiceSnapshot(Guid.NewGuid(), "Deicing"),
            [employee ?? new StaffMemberSnapshot(Guid.NewGuid(), "Report Engineer", "ENG-1")],
            TimeWindow.Create(Now.AddMinutes(10), Now.AddMinutes(35)).Value,
            null);

    private static WorkOrderTaskInput TaskInput() =>
        new(
            Id: null,
            TaskType.Minor,
            "Task without a service line",
            TimeWindow.Create(Now.AddMinutes(10), Now.AddMinutes(35)).Value,
            Employees: [],
            Tools: [],
            Materials: [],
            GeneralSupports: []);

    private static void TransitionWorkOrder(WorkOrder workOrder, WorkOrderStatus status)
    {
        switch (status)
        {
            case WorkOrderStatus.Submitted:
                return;
            case WorkOrderStatus.Returned:
                workOrder.Approve(1, "DMM-0001", Guid.NewGuid(), Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
                workOrder.Return(Guid.NewGuid(), "Correction required", Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
                return;
            case WorkOrderStatus.Approved:
                workOrder.Approve(1, "DMM-0001", Guid.NewGuid(), Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
                return;
            case WorkOrderStatus.Merged:
                workOrder.MarkMergedInto(Guid.NewGuid(), Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }
    }

    private static ServiceSnapshot PerLandingService() =>
        new(WellKnownMasterDataIds.AircraftPerLandingService, "Aircraft Per Landing");

    private sealed class StaticScope(OperationsScopeContext context) : IOperationsScope
    {
        public Task<Result<OperationsScopeContext>> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(context));
    }

    private sealed class StaticUserContext : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public BuildingBlocks.Contracts.Authorization.UserType? UserType =>
            BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => true;
    }
}
