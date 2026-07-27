using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Authorization;
using Operations.Application.Contracts;
using Operations.Application.Features.Dashboard;
using Operations.Application.Features.Flights;
using Operations.Application.Features.WorkOrders;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class DashboardQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Dashboard_ReturnsEveryOperationalFlightStatus()
    {
        await using var db = NewDb();
        var scheduled = CreateFlight("100");
        var inProgress = CreateFlight("200");
        var completed = CreateFlight("300");
        var canceled = CreateFlight("400");

        inProgress.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        completed.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        completed.SettleCompleted(Now).IsSuccess.ShouldBeTrue();
        canceled.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        canceled.SettleCanceled(Now).IsSuccess.ShouldBeTrue();

        db.Flights.AddRange(scheduled, inProgress, completed, canceled);
        await db.SaveChangesAsync();

        var handler = new GetOperationsDashboardQueryHandler(
            db,
            new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null)));

        var result = await handler.Handle(new GetOperationsDashboardQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalFlights.ShouldBe(4);
        result.Value.Statuses.Sum(item => item.Percentage).ShouldBe(100d);
        result.Value.ScheduledFlights.ShouldBe(1);
        result.Value.InProgressFlights.ShouldBe(1);
        result.Value.CompletedFlights.ShouldBe(1);
        result.Value.CanceledFlights.ShouldBe(1);
        result.Value.Hourly.Count.ShouldBe(24);
        result.Value.Monthly.Count.ShouldBe(12);
    }

    [Fact]
    public async Task Dashboard_UsesTheSameAssignedAndPerLandingVisibilityAsFlightLists()
    {
        await using var db = NewDb();
        var stationId = Guid.NewGuid();
        var otherStationId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var assignedStaff = new StaffMemberSnapshot(staffId, "Scoped Engineer", "EMP-10");

        var assigned = CreateFlight("100", stationId, assignedEmployees: [assignedStaff]);
        var perLanding = CreateFlight(
            "200",
            stationId,
            plannedServices: [new ServiceSnapshot(WellKnownMasterDataIds.AircraftPerLandingService, "Aircraft Per Landing")]);
        var unassigned = CreateFlight("300", stationId);
        var otherStation = CreateFlight(
            "400",
            otherStationId,
            assignedEmployees: [new StaffMemberSnapshot(staffId, "Scoped Engineer", "EMP-10")]);

        db.Flights.AddRange(assigned, perLanding, unassigned, otherStation);
        await db.SaveChangesAsync();

        var scope = new StaticScope(new OperationsScopeContext(UserType.StationStaff, stationId, staffId));
        var handler = new GetOperationsDashboardQueryHandler(db, scope);

        var result = await handler.Handle(new GetOperationsDashboardQuery(), CancellationToken.None);
        var listResult = await new GetFlightsQueryHandler(db, scope).Handle(
            new GetFlightsQuery(PageSize: 100),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        listResult.IsSuccess.ShouldBeTrue();
        listResult.Value.Items.Select(flight => flight.FlightNumber).ShouldBe(["100", "200"], ignoreOrder: true);
        result.Value.ScheduledFlights.ShouldBe(2);
        result.Value.InProgressFlights.ShouldBe(0);
        result.Value.CompletedFlights.ShouldBe(0);
        result.Value.CanceledFlights.ShouldBe(0);
    }

    [Fact]
    public async Task Dashboard_SummaryOnlyKeepsLegacyStatusCountsWithoutAnalyticsPayload()
    {
        await using var db = NewDb();
        db.Flights.AddRange(CreateFlight("100"), CreateFlight("200"));
        await db.SaveChangesAsync();

        var result = await AdminHandler(db).Handle(
            new GetOperationsDashboardQuery(IncludeAnalytics: false),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalFlights.ShouldBe(2);
        result.Value.ScheduledFlights.ShouldBe(2);
        result.Value.Stations.ShouldBeEmpty();
        result.Value.Customers.ShouldBeEmpty();
        result.Value.Services.ShouldBeEmpty();
        result.Value.OperationTypes.ShouldBeEmpty();
        result.Value.ServiceCategories.ShouldBeEmpty();
        result.Value.Timeline.ShouldBeEmpty();
        result.Value.TimelineGranularity.ShouldBe("Month");
        result.Value.Hourly.ShouldBeEmpty();
        result.Value.StationOptions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dashboard_AppliesHalfOpenMultiSelectAndPerformedServiceFilters()
    {
        await using var db = NewDb();
        var stationA = Guid.NewGuid();
        var stationB = Guid.NewGuid();
        var stationC = Guid.NewGuid();
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var serviceA = new ServiceSnapshot(Guid.NewGuid(), "Baggage");
        var serviceB = new ServiceSnapshot(Guid.NewGuid(), "Pushback");

        var first = CreateFlight(
            "100",
            stationA,
            customerId: customerA,
            scheduledArrival: Now);
        var second = CreateFlight(
            "200",
            stationB,
            customerId: customerB,
            scheduledArrival: Now.AddHours(1));
        var exclusiveBoundary = CreateFlight(
            "300",
            stationA,
            customerId: customerA,
            scheduledArrival: Now.AddHours(2));
        var wrongStation = CreateFlight(
            "400",
            stationC,
            customerId: customerA,
            scheduledArrival: Now.AddMinutes(30));
        var merged = CreateFlight(
            "500",
            stationA,
            customerId: customerA,
            scheduledArrival: Now.AddMinutes(45));
        var mergedWorkOrder = CreateWorkOrder(merged, serviceA);
        merged.MarkMergedInto(first.Id, Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();

        db.Flights.AddRange(first, second, exclusiveBoundary, wrongStation, merged);
        db.WorkOrders.AddRange(
            CreateWorkOrder(first, serviceA),
            CreateWorkOrder(second, serviceB),
            CreateWorkOrder(exclusiveBoundary, serviceA),
            CreateWorkOrder(wrongStation, serviceA),
            mergedWorkOrder);
        await db.SaveChangesAsync();

        var result = await AdminHandler(db).Handle(
            new GetOperationsDashboardQuery(
                FromUtc: Now,
                ToUtc: Now.AddHours(2),
                StationIds: [stationA, stationB],
                CustomerIds: [customerA, customerB],
                ServiceIds: [serviceA.ServiceId, serviceB.ServiceId]),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalFlights.ShouldBe(2);
        result.Value.FromUtc.ShouldBe(Now);
        result.Value.ToUtc.ShouldBe(Now.AddHours(2));
        result.Value.Customers.Select(item => item.Id!.Value).ShouldBe([customerA, customerB], ignoreOrder: true);
        result.Value.Services.Select(item => item.Id!.Value).ShouldBe(
            [serviceA.ServiceId, serviceB.ServiceId],
            ignoreOrder: true);
    }

    [Fact]
    public async Task Dashboard_TopFiveAddsDeterministicOtherGroup()
    {
        await using var db = NewDb();
        var customerIds = Enumerable.Range(1, 7).Select(_ => Guid.NewGuid()).ToArray();
        var flights = customerIds
            .Select((customerId, index) => CreateFlight(
                (100 + index).ToString(),
                customerId: customerId,
                customerName: $"Customer {index + 1:00}",
                customerIata: $"C{index + 1}"))
            .ToArray();
        db.Flights.AddRange(flights);
        await db.SaveChangesAsync();

        var result = await AdminHandler(db).Handle(
            new GetOperationsDashboardQuery(IncludeOptions: false),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CustomerOptions.ShouldBeEmpty();
        result.Value.Customers.Count.ShouldBe(6);
        result.Value.Customers.Take(5).Select(item => item.Label).ShouldBe(
            ["Customer 01", "Customer 02", "Customer 03", "Customer 04", "Customer 05"]);
        var other = result.Value.Customers.Last();
        other.IsOther.ShouldBeTrue();
        other.Id.ShouldBeNull();
        other.FlightCount.ShouldBe(2);
        other.GroupedItemCount.ShouldBe(2);
        other.Percentage.ShouldBe(28.57, 0.001);
    }

    [Fact]
    public async Task Dashboard_ReturnsAllStationsAndPutsAdHocFirstAmongAllOperationTypes()
    {
        await using var db = NewDb();
        var turnaroundId = Guid.NewGuid();
        var transitId = Guid.NewGuid();
        var specifications = new[]
        {
            (OperationTypeId: WellKnownMasterDataIds.AdHocOperationType, OperationTypeName: "Ad Hoc"),
            (OperationTypeId: turnaroundId, OperationTypeName: "Turnaround"),
            (OperationTypeId: turnaroundId, OperationTypeName: "Turnaround"),
            (OperationTypeId: turnaroundId, OperationTypeName: "Turnaround"),
            (OperationTypeId: transitId, OperationTypeName: "Transit"),
            (OperationTypeId: transitId, OperationTypeName: "Transit")
        };
        var flights = specifications
            .Select((specification, index) => CreateFlight(
                (100 + index).ToString(),
                stationId: Guid.NewGuid(),
                stationIata: $"S{index + 1:00}",
                stationName: $"Station {index + 1:00}",
                operationTypeId: specification.OperationTypeId,
                operationTypeName: specification.OperationTypeName))
            .ToArray();
        db.Flights.AddRange(flights);
        await db.SaveChangesAsync();

        var result = await AdminHandler(db).Handle(
            new GetOperationsDashboardQuery(TopCount: 1, IncludeOptions: false),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Stations.Count.ShouldBe(6);
        result.Value.Stations.ShouldAllBe(station => !station.IsOther);
        result.Value.Customers.Count.ShouldBe(2);
        result.Value.Customers.Last().IsOther.ShouldBeTrue();
        result.Value.OperationTypes.Select(item => item.Id).ShouldBe(
        [
            WellKnownMasterDataIds.AdHocOperationType,
            turnaroundId,
            transitId
        ]);
        result.Value.OperationTypes.Select(item => item.FlightCount).ShouldBe([1, 3, 2]);
        result.Value.OperationTypes.ShouldAllBe(item => !item.IsOther);
    }

    [Fact]
    public async Task Dashboard_UsesCanonicalMutuallyExclusivePerLandingAndOnCallCategories()
    {
        await using var db = NewDb();
        static ServiceSnapshot PerLandingService() => new(
            WellKnownMasterDataIds.AircraftPerLandingService,
            "Aircraft Per Landing");
        var performedService = new ServiceSnapshot(Guid.NewGuid(), "Baggage");
        var plainPerLanding = CreateFlight("100", plannedServices: [PerLandingService()]);
        var emptyWorkOrderPerLanding = CreateFlight("200", plannedServices: [PerLandingService()]);
        var mergedWorkOrderPerLanding = CreateFlight("300", plannedServices: [PerLandingService()]);
        var onCall = CreateFlight("400", plannedServices: [PerLandingService()]);
        var other = CreateFlight("500");
        var emptyWorkOrder = CreateWorkOrder(emptyWorkOrderPerLanding);
        var mergedWorkOrder = CreateWorkOrder(mergedWorkOrderPerLanding, performedService);
        mergedWorkOrder.MarkMergedInto(Guid.NewGuid(), Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();

        db.Flights.AddRange(
            plainPerLanding,
            emptyWorkOrderPerLanding,
            mergedWorkOrderPerLanding,
            onCall,
            other);
        db.WorkOrders.AddRange(
            emptyWorkOrder,
            mergedWorkOrder,
            CreateWorkOrder(onCall, performedService),
            CreateWorkOrder(other, performedService));
        await db.SaveChangesAsync();

        var result = await AdminHandler(db).Handle(
            new GetOperationsDashboardQuery(IncludeOptions: false),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalFlights.ShouldBe(5);
        result.Value.ServiceCategories.Select(item => item.Label).ShouldBe(["Per Landing", "On Call"]);
        result.Value.ServiceCategories.Select(item => item.FlightCount).ShouldBe([3, 1]);
        result.Value.ServiceCategories.Select(item => item.Percentage).ShouldBe([75d, 25d]);
        result.Value.ServiceCategories.ShouldAllBe(item => !item.IsOther);
    }

    [Fact]
    public async Task Dashboard_PerformedServicesCountDistinctFlightServicePairsAndIgnoreMergedWorkOrders()
    {
        await using var db = NewDb();
        var baggage = new ServiceSnapshot(Guid.NewGuid(), "Baggage");
        var pushback = new ServiceSnapshot(Guid.NewGuid(), "Pushback");
        var mergedOnly = new ServiceSnapshot(Guid.NewGuid(), "Merged only");
        var first = CreateFlight("100");
        var second = CreateFlight("200");
        var noServices = CreateFlight("300");
        var firstWorkOrder = CreateWorkOrder(first, baggage, baggage, pushback);
        var secondWorkOrder = CreateWorkOrder(second, baggage);
        var mergedWorkOrder = CreateWorkOrder(noServices, mergedOnly);
        mergedWorkOrder.MarkMergedInto(Guid.NewGuid(), Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();

        db.Flights.AddRange(first, second, noServices);
        db.WorkOrders.AddRange(firstWorkOrder, secondWorkOrder, mergedWorkOrder);
        await db.SaveChangesAsync();

        var result = await AdminHandler(db).Handle(
            new GetOperationsDashboardQuery(TopCount: 1),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalFlights.ShouldBe(3);
        result.Value.FlightsWithPerformedServices.ShouldBe(2);
        result.Value.Services.Count.ShouldBe(2);
        var top = result.Value.Services[0];
        top.Id.ShouldBe(baggage.ServiceId);
        top.FlightCount.ShouldBe(2);
        top.Percentage.ShouldBe(66.67, 0.001);
        var other = result.Value.Services[1];
        other.IsOther.ShouldBeTrue();
        other.FlightCount.ShouldBe(1);
        other.GroupedItemCount.ShouldBe(1);
        result.Value.ServiceOptions.Select(option => option.Id).ShouldBe(
            [baggage.ServiceId, pushback.ServiceId],
            ignoreOrder: true);
    }

    [Fact]
    public async Task Dashboard_ZeroFillsHourlyMonthlyAndYearlyBuckets()
    {
        await using var db = NewDb();
        db.Flights.AddRange(
            CreateFlight("100", scheduledArrival: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            CreateFlight("200", scheduledArrival: new DateTimeOffset(2026, 3, 1, 5, 0, 0, TimeSpan.Zero)),
            CreateFlight("300", scheduledArrival: new DateTimeOffset(2026, 3, 2, 5, 30, 0, TimeSpan.Zero)),
            CreateFlight("400", scheduledArrival: new DateTimeOffset(2027, 12, 1, 23, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();

        var result = await AdminHandler(db).Handle(
            new GetOperationsDashboardQuery(
                FromUtc: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                ToUtc: new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Hourly.Count.ShouldBe(24);
        result.Value.Hourly.Single(point => point.Key == "05").FlightCount.ShouldBe(2);
        result.Value.Hourly.Single(point => point.Key == "06").FlightCount.ShouldBe(0);
        result.Value.Monthly.Count.ShouldBe(12);
        result.Value.Monthly.Single(point => point.Key == "03").FlightCount.ShouldBe(2);
        result.Value.Monthly.Single(point => point.Key == "02").FlightCount.ShouldBe(0);
        result.Value.Yearly.Select(point => point.Key).ShouldBe(["2025", "2026", "2027"]);
        result.Value.Yearly.Select(point => point.FlightCount).ShouldBe([1, 2, 1]);
        result.Value.TimelineGranularity.ShouldBe("Month");
        result.Value.Timeline.Count.ShouldBe(36);
        result.Value.Timeline[0].BucketUtc.ShouldBe(
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        result.Value.Timeline.Single(point =>
                point.BucketUtc == new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero))
            .FlightCount.ShouldBe(0);
        result.Value.Timeline[^1].BucketUtc.ShouldBe(
            new DateTimeOffset(2027, 12, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(2, "Hour")]
    [InlineData(3, "Day")]
    [InlineData(120, "Day")]
    [InlineData(121, "Month")]
    public void Dashboard_TimelineGranularityUsesRequestedRange(int days, string expected)
    {
        DashboardTimelineProjection
            .SelectGranularity(Now, Now.AddDays(days))
            .ToString()
            .ShouldBe(expected);
    }

    [Fact]
    public async Task Dashboard_TimelineZeroFillsEveryIntersectingHourlyBucket()
    {
        await using var db = NewDb();
        var fromUtc = new DateTimeOffset(2026, 7, 12, 10, 30, 0, TimeSpan.Zero);
        var toUtc = new DateTimeOffset(2026, 7, 12, 13, 15, 0, TimeSpan.Zero);
        db.Flights.AddRange(
            CreateFlight("100", scheduledArrival: fromUtc),
            CreateFlight("200", scheduledArrival: new DateTimeOffset(2026, 7, 12, 12, 45, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();

        var result = await AdminHandler(db).Handle(
            new GetOperationsDashboardQuery(
                FromUtc: fromUtc,
                ToUtc: toUtc,
                IncludeOptions: false),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TimelineGranularity.ShouldBe("Hour");
        result.Value.Timeline.Select(point => point.BucketUtc).ShouldBe(
        [
            new DateTimeOffset(2026, 7, 12, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 12, 11, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 12, 13, 0, 0, TimeSpan.Zero)
        ]);
        result.Value.Timeline.Select(point => point.FlightCount).ShouldBe([1, 0, 1, 0]);
    }

    [Fact]
    public async Task Dashboard_MaxTimelineUsesHourlyBucketsWhenAllDataIsInOneUtcDay()
    {
        await using var db = NewDb();
        db.Flights.AddRange(
            CreateFlight(
                "100",
                scheduledArrival: new DateTimeOffset(2026, 7, 12, 3, 15, 0, TimeSpan.Zero)),
            CreateFlight(
                "200",
                scheduledArrival: new DateTimeOffset(2026, 7, 12, 6, 45, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();

        var result = await AdminHandler(db).Handle(
            new GetOperationsDashboardQuery(IncludeOptions: false),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TimelineGranularity.ShouldBe("Hour");
        result.Value.Timeline.Select(point => point.BucketUtc).ShouldBe(
        [
            new DateTimeOffset(2026, 7, 12, 3, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 12, 4, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 12, 5, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 12, 6, 0, 0, TimeSpan.Zero)
        ]);
        result.Value.Timeline.Select(point => point.FlightCount).ShouldBe([1, 0, 0, 1]);
    }

    [Fact]
    public async Task Dashboard_MaxTimelineUsesDailyBucketsWhenAllDataIsInOneUtcMonth()
    {
        await using var db = NewDb();
        db.Flights.AddRange(
            CreateFlight(
                "100",
                scheduledArrival: new DateTimeOffset(2026, 7, 2, 3, 15, 0, TimeSpan.Zero)),
            CreateFlight(
                "200",
                scheduledArrival: new DateTimeOffset(2026, 7, 5, 6, 45, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();

        var result = await AdminHandler(db).Handle(
            new GetOperationsDashboardQuery(IncludeOptions: false),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TimelineGranularity.ShouldBe("Day");
        result.Value.Timeline.Select(point => point.BucketUtc).ShouldBe(
        [
            new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 3, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 4, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero)
        ]);
        result.Value.Timeline.Select(point => point.FlightCount).ShouldBe([1, 0, 0, 1]);
    }

    [Fact]
    public async Task Dashboard_MaxTimelineUsesContinuousMonthlyBucketsAcrossActualData()
    {
        await using var db = NewDb();
        db.Flights.AddRange(
            CreateFlight(
                "100",
                scheduledArrival: new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero)),
            CreateFlight(
                "200",
                scheduledArrival: new DateTimeOffset(2025, 3, 20, 0, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();

        var result = await AdminHandler(db).Handle(
            new GetOperationsDashboardQuery(IncludeOptions: false),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TimelineGranularity.ShouldBe("Month");
        result.Value.Timeline.Select(point => point.BucketUtc).ShouldBe(
        [
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero)
        ]);
        result.Value.Timeline.Select(point => point.FlightCount).ShouldBe([1, 0, 1]);
    }

    [Fact]
    public async Task DashboardFlights_UsesIdenticalFiltersPagingSortingAndPerformedServiceRows()
    {
        await using var db = NewDb();
        var stationId = Guid.NewGuid();
        var baggage = new ServiceSnapshot(Guid.NewGuid(), "Baggage");
        var pushback = new ServiceSnapshot(Guid.NewGuid(), "Pushback");
        var matching = CreateFlight("100", stationId, scheduledArrival: Now);
        var notMatching = CreateFlight("200", stationId, scheduledArrival: Now.AddHours(1));
        db.Flights.AddRange(matching, notMatching);
        db.WorkOrders.AddRange(
            CreateWorkOrder(matching, pushback, baggage, baggage),
            CreateWorkOrder(notMatching, pushback));
        await db.SaveChangesAsync();
        var scope = new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null));

        var paged = await new GetDashboardFlightsQueryHandler(db, scope).Handle(
            new GetDashboardFlightsQuery(
                Page: 1,
                PageSize: 10,
                FromUtc: Now,
                ToUtc: Now.AddHours(2),
                StationIds: [stationId],
                ServiceIds: [baggage.ServiceId],
                Sort: "flightnumber:asc"),
            CancellationToken.None);
        var exported = await new GetDashboardFlightsExportQueryHandler(db, scope).Handle(
            new GetDashboardFlightsExportQuery(
                FromUtc: Now,
                ToUtc: Now.AddHours(2),
                StationIds: [stationId],
                ServiceIds: [baggage.ServiceId],
                Sort: "flightnumber:asc"),
            CancellationToken.None);

        paged.IsSuccess.ShouldBeTrue();
        exported.IsSuccess.ShouldBeTrue();
        paged.Value.TotalCount.ShouldBe(1);
        paged.Value.Items.ShouldHaveSingleItem().Id.ShouldBe(matching.Id);
        paged.Value.Items[0].PerformedServiceNames.ShouldBe(["Baggage", "Pushback"]);
        exported.Value.ShouldHaveSingleItem().Id.ShouldBe(paged.Value.Items[0].Id);
        exported.Value[0].PerformedServiceNames.ShouldBe(paged.Value.Items[0].PerformedServiceNames);
    }

    [Fact]
    public async Task Dashboard_RejectsEmptyOrReversedHalfOpenRange()
    {
        await using var db = NewDb();
        var handler = AdminHandler(db);

        var empty = await handler.Handle(
            new GetOperationsDashboardQuery(FromUtc: Now, ToUtc: Now),
            CancellationToken.None);
        var reversed = await new GetDashboardFlightsQueryHandler(
            db,
            new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null))).Handle(
            new GetDashboardFlightsQuery(FromUtc: Now, ToUtc: Now.AddMinutes(-1)),
            CancellationToken.None);

        empty.IsFailure.ShouldBeTrue();
        empty.Error.Code.ShouldBe("Operations.Dashboard.DateRangeInvalid");
        reversed.IsFailure.ShouldBeTrue();
        reversed.Error.Code.ShouldBe("Operations.Dashboard.DateRangeInvalid");
    }

    [Fact]
    public void Dashboard_ComposedQueryShapesAreSqlServerTranslatable()
    {
        using var db = new OperationsDbContext(
            new DbContextOptionsBuilder<OperationsDbContext>()
                .UseSqlServer("Server=localhost;Database=dashboard-translation;User Id=sa;Password=NotUsed1!;TrustServerCertificate=true")
                .Options);
        var performedWorkOrders = DashboardFlightQuery.PerformedWorkOrders(db);
        var performedServiceLines = DashboardFlightQuery.PerformedServiceLines(db);
        var qualifyingOnCallWorkOrders = db.WorkOrders.AsNoTracking().QualifyingForOnCall();
        var scopedFlights = DashboardFlightQuery.ApplyScope(
            db.Flights.AsNoTracking(),
            new OperationsScopeContext(UserType.SystemAdministrator, null, null),
            performedWorkOrders);
        var flights = DashboardFlightQuery.ApplyFilters(
            scopedFlights,
            DashboardFilter.Create(Now, Now.AddDays(1), [Guid.NewGuid()], [Guid.NewGuid()], [Guid.NewGuid()]),
            performedWorkOrders,
            performedServiceLines);
        var selectedFlights = flights
            .OrderByDescending(flight => flight.Schedule.Sta)
            .ThenBy(flight => flight.Id)
            .Skip(0)
            .Take(20);
        var selectedFlightIds = selectedFlights.Select(flight => flight.Id);
        var performedServicesForFlights =
            from workOrder in performedWorkOrders
            join line in performedServiceLines
                on workOrder.Id equals line.WorkOrderId
            where flights.Any(flight => flight.Id == workOrder.FlightId)
            select new
            {
                workOrder.FlightId,
                ServiceId = line.Service.ServiceId,
                ServiceName = line.Service.Name
            };

        var sql = new[]
        {
            flights.GroupBy(flight => flight.Status)
                .Select(group => new DashboardStatusCount(group.Key, group.LongCount()))
                .ToQueryString(),
            flights.GroupBy(flight => flight.Station.StationId)
                .Select(group => new DashboardGroupRow(
                    group.Key,
                    group.Max(flight => flight.Station.Name)!,
                    group.Max(flight => flight.Station.IataCode),
                    group.LongCount()))
                .ToQueryString(),
            flights.GroupBy(flight => flight.OperationType.OperationTypeId)
                .Select(group => new DashboardGroupRow(
                    group.Key,
                    group.Max(flight => flight.OperationType.Name)!,
                    null,
                    group.LongCount()))
                .ToQueryString(),
            flights
                .Where(flight => flight.PlannedServices.Any(service =>
                    service.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService))
                .Where(flight => qualifyingOnCallWorkOrders.Any(workOrder =>
                    workOrder.FlightId == flight.Id))
                .ToQueryString(),
            performedServicesForFlights
                .GroupBy(service => service.ServiceId)
                .Select(group => new DashboardGroupRow(
                    group.Key,
                    group.Max(service => service.ServiceName)!,
                    null,
                    group.Select(service => service.FlightId).Distinct().LongCount()))
                .ToQueryString(),
            flights.GroupBy(flight => flight.Schedule.Sta.Hour)
                .Select(group => new DashboardTrendCount(group.Key, group.LongCount()))
                .ToQueryString(),
            flights.GroupBy(_ => 1)
                .Select(group => new DashboardTimelineBounds(
                    group.Min(flight => flight.Schedule.Sta),
                    group.Max(flight => flight.Schedule.Sta)))
                .ToQueryString(),
            flights.GroupBy(flight => new
                {
                    flight.Schedule.Sta.Year,
                    flight.Schedule.Sta.Month,
                    flight.Schedule.Sta.Day,
                    flight.Schedule.Sta.Hour
                })
                .Select(group => new DashboardTimelineCount(
                    group.Key.Year,
                    group.Key.Month,
                    group.Key.Day,
                    group.Key.Hour,
                    group.LongCount()))
                .ToQueryString(),
            (from workOrder in performedWorkOrders
                join line in performedServiceLines
                    on workOrder.Id equals line.WorkOrderId
                where scopedFlights.Any(flight => flight.Id == workOrder.FlightId)
                group line by line.Service.ServiceId
                into serviceGroup
                select new DashboardFilterOptionDto(
                    serviceGroup.Key,
                    serviceGroup.Max(line => line.Service.Name)!,
                    null))
                .ToQueryString(),
            selectedFlights
                .Select(flight => new DashboardFlightBaseRow(
                    flight.Id,
                    flight.FlightNumber.Value,
                    flight.Customer.IataCode,
                    flight.Customer.Name,
                    flight.Station.StationId,
                    flight.Station.IataCode,
                    flight.Station.Name,
                    flight.OperationType.Name,
                    flight.Schedule.Sta,
                    flight.Schedule.Std,
                    flight.Status.ToString()))
                .ToQueryString(),
            (from workOrder in performedWorkOrders
                join line in performedServiceLines
                    on workOrder.Id equals line.WorkOrderId
                where selectedFlightIds.Contains(workOrder.FlightId)
                select new DashboardFlightServiceRow(
                    workOrder.FlightId,
                    line.Service.ServiceId,
                    line.Service.Name))
                .ToQueryString()
        };

        sql.ShouldAllBe(statement => statement.Contains("SELECT", StringComparison.OrdinalIgnoreCase));
    }

    private static OperationsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OperationsDbContext>()
            .UseInMemoryDatabase($"dashboard-{Guid.NewGuid()}")
            .Options);

    private static Flight CreateFlight(
        string flightNumber,
        Guid? stationId = null,
        IReadOnlyList<StaffMemberSnapshot>? assignedEmployees = null,
        IReadOnlyList<ServiceSnapshot>? plannedServices = null,
        Guid? customerId = null,
        string customerIata = "RJ",
        string customerName = "Royal Jordanian",
        DateTimeOffset? scheduledArrival = null,
        string stationIata = "ORD",
        string stationName = "Chicago O'Hare",
        Guid? operationTypeId = null,
        string operationTypeName = "Transit") =>
        Flight.ScheduleNew(
            new CustomerSnapshot(customerId ?? Guid.NewGuid(), customerIata, customerName),
            new StationSnapshot(stationId ?? Guid.NewGuid(), stationIata, stationName),
            new OperationTypeSnapshot(operationTypeId ?? Guid.NewGuid(), operationTypeName),
            FlightNumber.Create(flightNumber).Value,
            ScheduledTime.Create(scheduledArrival ?? Now, (scheduledArrival ?? Now).AddHours(1)).Value,
            aircraftType: null,
            plannedServices: plannedServices ?? [new ServiceSnapshot(Guid.NewGuid(), "Marshalling")],
            assignedEmployees: assignedEmployees ?? [],
            contractId: null,
            contractNumber: null,
            createdByUserId: Guid.NewGuid(),
            now: Now).Value;

    private static WorkOrder CreateWorkOrder(Flight flight, params ServiceSnapshot[] services)
    {
        var employee = new StaffMemberSnapshot(Guid.NewGuid(), "Ramp Engineer", "ENG-1");
        var serviceLines = services.Select(service => new WorkOrderServiceLineInput(
            new ServiceSnapshot(service.ServiceId, service.Name),
            [new StaffMemberSnapshot(employee.StaffMemberId, employee.FullName, employee.EmployeeId)],
            TimeWindow.Create(flight.Schedule.Sta, flight.Schedule.Std).Value,
            Description: null)).ToList();
        return WorkOrder.SubmitNew(
            flight,
            WorkOrderType.Completion,
            Guid.NewGuid(),
            employee,
            FlightNumber.Create(flight.FlightNumber.Value).Value,
            new AircraftTypeSnapshot(Guid.NewGuid(), "Airbus", "A320"),
            "N123AB",
            ActualTime.Create(flight.Schedule.Sta, flight.Schedule.Std).Value,
            cancellation: null,
            remarks: null,
            serviceLines,
            tasks: [],
            now: Now).Value;
    }

    private static GetOperationsDashboardQueryHandler AdminHandler(OperationsDbContext db) =>
        new(
            db,
            new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null)));

    private sealed class StaticScope(OperationsScopeContext context) : IOperationsScope
    {
        public Task<Result<OperationsScopeContext>> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(context));
    }
}
