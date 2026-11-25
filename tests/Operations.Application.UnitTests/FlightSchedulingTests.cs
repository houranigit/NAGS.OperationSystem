using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Features.Flights;
using Operations.Contracts;
using Operations.Domain.Authorization;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class FlightSchedulingTests
{
    [Theory]
    [InlineData("America/Chicago", 16, 15)]
    [InlineData("Etc/UTC", 10, 10)]
    [InlineData("UTC", 10, 10)]
    public async Task BatchSchedule_UsesTheStationZoneInsteadOfTheSuppliedBrowserZoneAcrossDst(string zone, int winterHour, int summerHour)
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader { StationTimeZoneId = zone };
        var result = await new ScheduleFlightsCommandHandler(db, AdminScope(), new MasterDataResolver(reader),
            new TestUser(), new AuditActor(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new ScheduleFlightsCommand(Guid.NewGuid(), reader.StationId, Guid.NewGuid(), "123",
                new TimeOnly(10, 0), new TimeOnly(11, 0),
                [new DateOnly(2026, 1, 15), new DateOnly(2026, 7, 15)],
                null, [reader.ServiceId], [], "Asia/Riyadh"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var arrivals = await db.Flights.OrderBy(f => f.Schedule.Sta).Select(f => f.Schedule.Sta).ToListAsync();
        arrivals.ShouldBe([
            new DateTimeOffset(2026, 1, 15, winterHour, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 15, summerHour, 0, 0, TimeSpan.Zero)]);
    }

    [Theory]
    [InlineData(null, "Operations.Ref.StationTimeZoneInvalid")]
    [InlineData("", "Operations.Ref.StationTimeZoneInvalid")]
    [InlineData("Browser UTC+03:00", "Operations.Ref.StationTimeZoneInvalid")]
    [InlineData("not/a-zone", "Operations.Ref.StationTimeZoneInvalid")]
    public async Task BatchSchedule_RejectsInvalidStationDataBeforeSaving(string? zone, string errorCode)
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader { StationTimeZoneId = zone };
        var result = await new ScheduleFlightsCommandHandler(db, AdminScope(), new MasterDataResolver(reader),
            new TestUser(), new AuditActor(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new ScheduleFlightsCommand(Guid.NewGuid(), reader.StationId, Guid.NewGuid(), "123",
                new TimeOnly(10, 0), new TimeOnly(11, 0), [new DateOnly(2026, 7, 15)],
                null, [reader.ServiceId], [], "Asia/Riyadh"), CancellationToken.None);

        result.Error.Code.ShouldBe(errorCode);
        (await db.Flights.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task FlightReads_UseCurrentStationConfigurationForExistingFlights()
    {
        await using var db = NewDb();
        var flight = NewFlight(Guid.NewGuid(), []);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        var reader = new SchedulingMasterDataReader { StationTimeZoneId = "Asia/Riyadh" };
        var handler = new GetFlightByIdQueryHandler(db, AdminScope(), reader);

        var initial = await handler.Handle(new GetFlightByIdQuery(flight.Id), CancellationToken.None);
        initial.Value.StationTimeZoneId.ShouldBe("Asia/Riyadh");
        reader.StationTimeZoneId = "America/Chicago";
        var updated = await handler.Handle(new GetFlightByIdQuery(flight.Id), CancellationToken.None);
        updated.Value.StationTimeZoneId.ShouldBe("America/Chicago");
        updated.Value.ScheduledArrivalUtc.ShouldBe(initial.Value.ScheduledArrivalUtc);
        var page = await new GetFlightsQueryHandler(db, AdminScope(), NoopCustomerLogoReader.Instance, reader)
            .Handle(new GetFlightsQuery(), CancellationToken.None);
        page.Value.Items.Single().StationId.ShouldBe(flight.Station.StationId);
        page.Value.Items.Single().StationTimeZoneId.ShouldBe("America/Chicago");
    }

    [Fact]
    public async Task FlightReads_RejectAnOrphanStationInsteadOfReturningMissingTimeZoneMetadata()
    {
        await using var db = NewDb();
        var flight = NewFlight(Guid.NewGuid(), []);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        var reader = new SchedulingMasterDataReader { StationExists = false };

        var detail = await new GetFlightByIdQueryHandler(db, AdminScope(), reader)
            .Handle(new GetFlightByIdQuery(flight.Id), CancellationToken.None);
        var page = await new GetFlightsQueryHandler(db, AdminScope(), NoopCustomerLogoReader.Instance, reader)
            .Handle(new GetFlightsQuery(), CancellationToken.None);

        detail.IsFailure.ShouldBeTrue();
        page.IsFailure.ShouldBeTrue();
        detail.Error.Code.ShouldBe("Operations.Flight.StationDataUnavailable");
        page.Error.Code.ShouldBe("Operations.Flight.StationDataUnavailable");
    }

    public static TheoryData<string?, string, string> ValidFlightNumbers => new()
    {
        { "SV", "456", "SV456" },
        { "SV", "SV456", "SV456" },
        { "SV", "sv456", "SV456" },
        { "SV", "456/1", "SV456/1" },
        { "SV", "SV456/12", "SV456/12" },
        { "5Y", "456/12", "5Y456/12" },
        { "5Y", "5y456", "5Y456" },
        { "5Y", "5Y", "5Y5Y" },
        { "12", "12", "1212" },
        { null, "456", "456" },
        { null, "456/12", "456/12" },
        { "SV", "456A", "SV456A" },
        { "SV", "1234a", "SV1234A" },
        { "SV", "sv1234a", "SV1234A" },
        { "5Y", "1234a", "5Y1234A" },
        { null, "1234a", "1234A" },
        { "SV", "1234/12", "SV1234/12" }
    };

    public static TheoryData<string?, string, string> InvalidFlightNumbers => new()
    {
        { "SV", "", "Operations.FlightNumber.Required" },
        { "SV", "  ", "Operations.FlightNumber.Required" },
        { "SV", "SV", "Operations.FlightNumber.InvalidFormat" },
        { "SV", "XY456", "Operations.FlightNumber.InvalidFormat" },
        { "SV", "SVSV456", "Operations.FlightNumber.InvalidFormat" },
        { "SV", "456/", "Operations.FlightNumber.InvalidFormat" },
        { "SV", "456/123", "Operations.FlightNumber.InvalidFormat" },
        { "SV", "456//1", "Operations.FlightNumber.InvalidFormat" },
        { "SV", "456 1", "Operations.FlightNumber.InvalidFormat" },
        { "SV", " 456", "Operations.FlightNumber.InvalidFormat" },
        { "SV", "456 ", "Operations.FlightNumber.InvalidFormat" },
        { "SV", "456-1", "Operations.FlightNumber.InvalidFormat" },
        { "SV", "456AB", "Operations.FlightNumber.InvalidFormat" },
        { "SV", "456A/1", "Operations.FlightNumber.InvalidFormat" },
        { null, "1234a/12", "Operations.FlightNumber.InvalidFormat" },
        { "SV", "456AB/1", "Operations.FlightNumber.InvalidFormat" },
        { "SV", "456ABC", "Operations.FlightNumber.InvalidFormat" },
        { "SV", "456@1", "Operations.FlightNumber.InvalidFormat" },
        { "SV", "٤٥٦", "Operations.FlightNumber.InvalidFormat" },
        { "SV", "12345", "Operations.FlightNumber.InvalidFormat" },
        { null, "SV456", "Operations.FlightNumber.InvalidFormat" },
        { "5Y", "SV456", "Operations.FlightNumber.InvalidFormat" }
    };

    [Theory]
    [MemberData(nameof(ValidFlightNumbers))]
    public async Task SingleFlight_PersistsTheNormalizedFullNumberAndCanBeReadFromTheGrid(string? customerCode, string flightNumber, string expectedNumber)
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader(customerCode);
        var now = new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);
        var result = await new ScheduleFlightCommandHandler(db, AdminScope(), new MasterDataResolver(reader), new TestUser(), new AuditActor(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new ScheduleFlightCommand(Guid.NewGuid(), reader.StationId, Guid.NewGuid(), flightNumber,
                now, now.AddHours(1), null, [reader.ServiceId], []), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        db.ChangeTracker.Clear();
        var flight = await db.Flights.Include(f => f.PlannedServices).SingleAsync();
        flight.FlightNumber.Value.ShouldBe(expectedNumber);
        flight.PlannedServices.ShouldHaveSingleItem().Service.ServiceId.ShouldBe(reader.ServiceId);
        var page = await new GetFlightsQueryHandler(db, AdminScope(), NoopCustomerLogoReader.Instance, new SchedulingMasterDataReader())
            .Handle(new GetFlightsQuery(Search: expectedNumber), CancellationToken.None);
        var item = page.Value.Items.ShouldHaveSingleItem();
        item.Id.ShouldBe(result.Value);
        item.FlightNumber.ShouldBe(expectedNumber);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("A310")]
    public async Task FlightRegister_IncludesTheOptionalAircraftModel(string? aircraftModel)
    {
        await using var db = NewDb();
        var aircraft = aircraftModel is null ? null : new AircraftTypeSnapshot(Guid.NewGuid(), "Airbus", aircraftModel);
        var flight = NewFlight(Guid.NewGuid(), [], aircraftType: aircraft);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new GetFlightsQueryHandler(db, AdminScope(), NoopCustomerLogoReader.Instance, new SchedulingMasterDataReader())
            .Handle(new GetFlightsQuery(), CancellationToken.None);

        var item = result.Value.Items.ShouldHaveSingleItem();
        item.AircraftTypeModel.ShouldBe(aircraftModel);
        item.CustomerId.ShouldBe(flight.Customer.CustomerId);
    }

    [Fact]
    public async Task GridStationSort_UsesTheColumnPropertyName()
    {
        await using var db = NewDb();
        var first = NewFlight(Guid.NewGuid(), [], stationIata: "AAA", arrivalDay: 1);
        var second = NewFlight(Guid.NewGuid(), [], stationIata: "ZZZ", arrivalDay: 2);
        db.Flights.AddRange(first, second);
        await db.SaveChangesAsync();

        var result = await new GetFlightsQueryHandler(db, AdminScope(), NoopCustomerLogoReader.Instance, new SchedulingMasterDataReader())
            .Handle(new GetFlightsQuery(Sort: "StationIataCode:asc"), CancellationToken.None);

        result.Value.Items.Select(flight => flight.Id).ShouldBe([first.Id, second.Id]);
    }

    [Theory]
    [InlineData(FlightListType.All)]
    [InlineData(FlightListType.Scheduled)]
    [InlineData(FlightListType.PerLanding)]
    [InlineData(FlightListType.AdHoc)]
    public async Task FlightRegister_AppliesTypeWithinPortalScopeBeforePaging(FlightListType type)
    {
        await using var db = NewDb();
        var stationId = Guid.NewGuid();
        var adHocTypeId = WellKnownMasterDataIds.AdHocOperationType;
        var scheduled = NewFlight(stationId, [], arrivalDay: 1);
        var perLanding = NewFlight(stationId, [], perLanding: true, arrivalDay: 2);
        var adHoc = NewFlight(stationId, [], arrivalDay: 3, operationTypeId: adHocTypeId);
        var adHocWithPerLandingService = NewFlight(stationId, [], perLanding: true, arrivalDay: 4, operationTypeId: adHocTypeId);
        db.Flights.AddRange(scheduled, perLanding, adHoc, adHocWithPerLandingService,
            NewFlight(Guid.NewGuid(), [], arrivalDay: 1, operationTypeId: adHocTypeId));
        await db.SaveChangesAsync();

        Flight[] expected = type switch
        {
            FlightListType.Scheduled => [scheduled],
            FlightListType.PerLanding => [perLanding],
            FlightListType.AdHoc => [adHoc, adHocWithPerLandingService],
            _ => [scheduled, perLanding, adHoc, adHocWithPerLandingService]
        };
        var scope = new StaticScope(new OperationsScopeContext(UserType.StationStaff, stationId, Guid.NewGuid(), CanViewStationWide: true));
        var handler = new GetFlightsQueryHandler(db, scope, NoopCustomerLogoReader.Instance, new SchedulingMasterDataReader());
        for (var page = 1; page <= expected.Length; page++)
        {
            var result = await handler.Handle(new GetFlightsQuery(Page: page, PageSize: 1,
                Sort: "scheduledArrivalUtc:asc", FlightType: type), CancellationToken.None);

            result.IsSuccess.ShouldBeTrue();
            result.Value.TotalCount.ShouldBe(expected.LongLength);
            var item = result.Value.Items.ShouldHaveSingleItem();
            item.Id.ShouldBe(expected[page - 1].Id);
            item.IsAdHocFlight.ShouldBe(item.Id == adHoc.Id || item.Id == adHocWithPerLandingService.Id);
        }
    }

    [Theory]
    [MemberData(nameof(ValidFlightNumbers))]
    public async Task BatchSchedule_NormalizesTheFullNumberDeduplicatesDatesAndPreservesOvernightLocalTimes(string? customerCode, string flightNumber, string expectedNumber)
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader(customerCode);
        var firstDate = new DateOnly(2026, 7, 3);
        var result = await new ScheduleFlightsCommandHandler(db, AdminScope(), new MasterDataResolver(reader), new TestUser(), new AuditActor(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new ScheduleFlightsCommand(Guid.NewGuid(), reader.StationId, Guid.NewGuid(), flightNumber,
                new TimeOnly(23, 30), new TimeOnly(1, 30), [firstDate.AddDays(1), firstDate, firstDate],
                null, [reader.ServiceId], [], "Asia/Riyadh"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        db.ChangeTracker.Clear();
        var flights = await db.Flights.Include(f => f.PlannedServices).OrderBy(f => f.Schedule.Sta).ToListAsync();
        flights[0].Schedule.Sta.ShouldBe(new DateTimeOffset(2026, 7, 3, 20, 30, 0, TimeSpan.Zero));
        flights[0].Schedule.Std.ShouldBe(new DateTimeOffset(2026, 7, 3, 22, 30, 0, TimeSpan.Zero));
        flights[1].Schedule.Sta.ShouldBe(flights[0].Schedule.Sta.AddDays(1));
        flights.ShouldAllBe(flight => flight.FlightNumber.Value == expectedNumber);
        flights.All(flight => flight.PlannedServices.Count == 1).ShouldBeTrue();
        flights[0].PlannedServices[0].Id.ShouldNotBe(flights[1].PlannedServices[0].Id);
        var page = await new GetFlightsQueryHandler(db, AdminScope(), NoopCustomerLogoReader.Instance, new SchedulingMasterDataReader())
            .Handle(new GetFlightsQuery(Search: expectedNumber), CancellationToken.None);
        page.Value.Items.Select(item => item.Id).ShouldBe(result.Value, ignoreOrder: true);
        page.Value.Items.ShouldAllBe(item => item.FlightNumber == expectedNumber);
    }

    [Theory]
    [MemberData(nameof(InvalidFlightNumbers))]
    public async Task SingleFlight_RejectsInvalidNumbersWithoutSaving(string? customerCode, string flightNumber, string expectedErrorCode)
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader(customerCode);
        var now = new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);

        var result = await new ScheduleFlightCommandHandler(db, AdminScope(), new MasterDataResolver(reader), new TestUser(), new AuditActor(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new ScheduleFlightCommand(Guid.NewGuid(), reader.StationId, Guid.NewGuid(), flightNumber,
                now, now.AddHours(1), null, [reader.ServiceId], []), CancellationToken.None);

        result.Error.Code.ShouldBe(expectedErrorCode);
        (await db.Flights.CountAsync()).ShouldBe(0);
        db.ChangeTracker.HasChanges().ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(InvalidFlightNumbers))]
    public async Task BatchSchedule_RejectsInvalidNumbersWithoutSavingAnyDate(string? customerCode, string flightNumber, string expectedErrorCode)
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader(customerCode);
        var date = new DateOnly(2026, 7, 3);

        var result = await new ScheduleFlightsCommandHandler(db, AdminScope(), new MasterDataResolver(reader), new TestUser(), new AuditActor(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new ScheduleFlightsCommand(Guid.NewGuid(), reader.StationId, Guid.NewGuid(), flightNumber,
                new TimeOnly(10, 0), new TimeOnly(11, 0), [date, date.AddDays(1)],
                null, [reader.ServiceId], []), CancellationToken.None);

        result.Error.Code.ShouldBe(expectedErrorCode);
        (await db.Flights.CountAsync()).ShouldBe(0);
        db.ChangeTracker.HasChanges().ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(ValidFlightNumbers))]
    public async Task ChangeFlightNumber_NormalizesTheFullNumberUsingTheSavedCustomerCode(string? customerCode, string flightNumber, string expectedNumber)
    {
        await using var db = NewDb();
        var flight = NewFlight(Guid.NewGuid(), [], customerCode: customerCode);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();

        var result = await new ChangeFlightNumberCommandHandler(db, AdminScope(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new ChangeFlightNumberCommand(flight.Id, flightNumber, flight.RowVersion), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        db.ChangeTracker.Clear();
        var saved = await db.Flights.SingleAsync();
        saved.FlightNumber.Value.ShouldBe(expectedNumber);
        saved.Customer.IataCode.ShouldBe(customerCode);
        saved.UpdatedAtUtc.ShouldNotBeNull();
    }

    [Theory]
    [MemberData(nameof(InvalidFlightNumbers))]
    public async Task ChangeFlightNumber_RejectsInvalidNumbersWithoutChangingTheFlight(string? customerCode, string flightNumber, string expectedErrorCode)
    {
        await using var db = NewDb();
        var flight = NewFlight(Guid.NewGuid(), [], customerCode: customerCode);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        var originalNumber = flight.FlightNumber.Value;

        var result = await new ChangeFlightNumberCommandHandler(db, AdminScope(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new ChangeFlightNumberCommand(flight.Id, flightNumber, flight.RowVersion), CancellationToken.None);

        result.Error.Code.ShouldBe(expectedErrorCode);
        flight.FlightNumber.Value.ShouldBe(originalNumber);
        flight.UpdatedAtUtc.ShouldBeNull();
        db.ChangeTracker.HasChanges().ShouldBeFalse();
        db.ChangeTracker.Clear();
        (await db.Flights.SingleAsync()).FlightNumber.Value.ShouldBe(originalNumber);
    }

    [Fact]
    public async Task ChangeFlightNumber_NormalizedCurrentNumberDoesNotChangeTheFlight()
    {
        await using var db = NewDb();
        var flight = NewFlight(Guid.NewGuid(), []);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();

        var result = await new ChangeFlightNumberCommandHandler(db, AdminScope(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new ChangeFlightNumberCommand(flight.Id, "123", flight.RowVersion), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        flight.FlightNumber.Value.ShouldBe("SV123");
        flight.UpdatedAtUtc.ShouldBeNull();
        db.ChangeTracker.HasChanges().ShouldBeFalse();
    }

    [Fact]
    public async Task SchedulingWithAssignedStaff_RequiresAssignmentPermission()
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader();
        var result = await new ScheduleFlightCommandHandler(db, AdminScope(), new MasterDataResolver(reader), new TestUser(false), new AuditActor(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new ScheduleFlightCommand(Guid.NewGuid(), reader.StationId, Guid.NewGuid(), "SV123",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), null, [reader.ServiceId], [Guid.NewGuid()]), CancellationToken.None);

        result.Error.Code.ShouldBe("Operations.Flight.AssignForbidden");
        (await db.Flights.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task SchedulingOutsideStationScope_IsDenied()
    {
        await using var db = NewDb();
        var scope = new StaticScope(new OperationsScopeContext(UserType.StationStaff, Guid.NewGuid(), Guid.NewGuid()));
        var reader = new SchedulingMasterDataReader();
        var result = await new ScheduleFlightCommandHandler(db, scope, new MasterDataResolver(reader), new TestUser(), new AuditActor(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new ScheduleFlightCommand(Guid.NewGuid(), reader.StationId, Guid.NewGuid(), "SV123",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), null, [reader.ServiceId], []), CancellationToken.None);

        result.Error.Code.ShouldBe("Operations.Scope.Forbidden");
        (await db.Flights.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task StationStaff_SeeOnlyAssignedOrPerLandingFlightsAtTheirStation()
    {
        await using var db = NewDb();
        var stationId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var assigned = NewFlight(stationId, [new StaffMemberSnapshot(staffId, "Assigned staff", "E1")]);
        var perLanding = NewFlight(stationId, [], perLanding: true);
        db.Flights.AddRange(assigned, perLanding, NewFlight(stationId, []), NewFlight(Guid.NewGuid(), [], perLanding: true));
        await db.SaveChangesAsync();

        var result = await new GetFlightsQueryHandler(db, new StaticScope(new OperationsScopeContext(UserType.StationStaff, stationId, staffId)), NoopCustomerLogoReader.Instance, new SchedulingMasterDataReader())
            .Handle(new GetFlightsQuery(), CancellationToken.None);

        result.Value.Items.Select(flight => flight.Id).ShouldBe([assigned.Id, perLanding.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task StationDispatcher_SeesAllFlightsAtTheirStation()
    {
        await using var db = NewDb();
        var stationId = Guid.NewGuid();
        var flight = NewFlight(stationId, []);
        db.Flights.AddRange(flight, NewFlight(Guid.NewGuid(), []));
        await db.SaveChangesAsync();

        var result = await new GetFlightsQueryHandler(db, new StaticScope(new OperationsScopeContext(UserType.StationStaff, stationId, Guid.NewGuid(), CanViewStationWide: true)), NoopCustomerLogoReader.Instance, new SchedulingMasterDataReader())
            .Handle(new GetFlightsQuery(), CancellationToken.None);

        result.Value.Items.ShouldHaveSingleItem().Id.ShouldBe(flight.Id);
    }

    [Fact]
    public async Task ScheduleEdit_CanClearRosterAndChangeToPerLandingTogether()
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader();
        var staff = new StaffMemberSnapshot(Guid.NewGuid(), "Assigned staff", "E1");
        var flight = NewFlight(reader.StationId, [staff]);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        var arrival = flight.Schedule.Sta.AddHours(2);

        var result = await new UpdateScheduledFlightCommandHandler(db, AdminScope(), new MasterDataResolver(reader), new TestUser(), new AuditActor(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new UpdateScheduledFlightCommand(flight.Id, flight.Customer.CustomerId, reader.StationId,
                flight.OperationType.OperationTypeId, arrival, arrival.AddHours(1), null,
                [WellKnownMasterDataIds.AircraftPerLandingService], flight.RowVersion, []), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        db.ChangeTracker.Clear();
        var saved = await db.Flights.Include(item => item.PlannedServices).Include(item => item.AssignedEmployees).SingleAsync();
        saved.IsPerLanding.ShouldBeTrue();
        saved.AssignedEmployees.ShouldBeEmpty();
        saved.Schedule.Sta.ShouldBe(arrival);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordedCancellationLocksTheScheduleButAllowsRosterReplacement(bool approve)
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader();
        var employee = new StaffMemberSnapshot(Guid.NewGuid(), "Assigned employee", "E1");
        var flight = NewFlight(reader.StationId, [employee]);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        var details = new GetFlightByIdQueryHandler(db, AdminScope(), reader);
        (await details.Handle(new(flight.Id), CancellationToken.None)).Value.CanEditSchedule.ShouldBeTrue();

        var now = TimeProvider.System.GetUtcNow();
        var cancel = CancellationDetails.Create(now, "Recorded cancellation").Value;
        flight.SubmitCancellation(employee, cancel, Guid.NewGuid(), now).IsSuccess.ShouldBeTrue();
        if (approve)
            flight.Approve(Guid.NewGuid(), "Reviewer", now, 1).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        (await details.Handle(new(flight.Id), CancellationToken.None)).Value.CanEditSchedule.ShouldBeFalse();
        var expectedError = approve ? "Operations.FlightApproval.Locked" : "Operations.Flight.ScheduleReadOnly";

        var resolver = new MasterDataResolver(reader);
        var update = await new UpdateScheduledFlightCommandHandler(db, AdminScope(), resolver, new TestUser(), new AuditActor(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new(flight.Id, flight.Customer.CustomerId, reader.StationId, flight.OperationType.OperationTypeId,
                flight.Schedule.Sta.AddHours(2), flight.Schedule.Std.AddHours(2), null, [reader.ServiceId], flight.RowVersion, []), CancellationToken.None);
        update.Error.Code.ShouldBe(expectedError);
        var rename = await new ChangeFlightNumberCommandHandler(db, AdminScope(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new(flight.Id, "5678", flight.RowVersion), CancellationToken.None);
        rename.Error.Code.ShouldBe(expectedError);

        var assigned = await new AssignEmployeesCommandHandler(db, AdminScope(), resolver, new TestUser(), new AuditActor(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new(flight.Id, [], flight.RowVersion), CancellationToken.None);
        assigned.IsSuccess.ShouldBeTrue();
        db.ChangeTracker.Clear();
        var saved = (await details.Handle(new(flight.Id), CancellationToken.None)).Value;
        saved.AssignedEmployees.ShouldBeEmpty();
        saved.ScheduledArrivalUtc.ShouldBe(flight.Schedule.Sta);
        saved.ScheduledDepartureUtc.ShouldBe(flight.Schedule.Std);
        saved.FlightNumber.ShouldBe(flight.FlightNumber.Value);
        saved.CancellationSubmissions!.ShouldHaveSingleItem();
        saved.ApprovalStatus.ShouldBe(approve ? FlightApprovalStatus.Approved : FlightApprovalStatus.NotApproved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScheduleEdit_WithoutAssignmentPermission_PreservesAnOmittedOrUnchangedRoster(bool includeRoster)
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader();
        var staff = new StaffMemberSnapshot(Guid.NewGuid(), "Assigned staff", "E1");
        var flight = NewFlight(reader.StationId, [staff]);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        var assignmentId = flight.AssignedEmployees[0].Id;
        var arrival = flight.Schedule.Sta.AddHours(2);

        var result = await new UpdateScheduledFlightCommandHandler(db, AdminScope(), new MasterDataResolver(reader), new TestUser(false), new AuditActor(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new UpdateScheduledFlightCommand(flight.Id, flight.Customer.CustomerId, reader.StationId,
                flight.OperationType.OperationTypeId, arrival, arrival.AddHours(1), null,
                [reader.ServiceId], flight.RowVersion, includeRoster ? [staff.StaffMemberId] : null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        db.ChangeTracker.Clear();
        var saved = await db.Flights.Include(item => item.AssignedEmployees).SingleAsync();
        saved.AssignedEmployees.ShouldHaveSingleItem().Id.ShouldBe(assignmentId);
        saved.Schedule.Sta.ShouldBe(arrival);
    }

    [Fact]
    public async Task ScheduleEdit_RejectsUnauthorizedRosterChangeBeforeChangingPlanning()
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader();
        var staff = new StaffMemberSnapshot(Guid.NewGuid(), "Assigned staff", "E1");
        var flight = NewFlight(reader.StationId, [staff]);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        var arrival = flight.Schedule.Sta;
        var serviceId = flight.PlannedServices[0].Service.ServiceId;

        var result = await new UpdateScheduledFlightCommandHandler(db, AdminScope(), new MasterDataResolver(reader), new TestUser(false), new AuditActor(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new UpdateScheduledFlightCommand(flight.Id, flight.Customer.CustomerId, reader.StationId,
                flight.OperationType.OperationTypeId, arrival.AddHours(2), arrival.AddHours(3), null,
                [WellKnownMasterDataIds.AircraftPerLandingService], flight.RowVersion, []), CancellationToken.None);

        result.Error.Code.ShouldBe("Operations.Flight.AssignForbidden");
        flight.Schedule.Sta.ShouldBe(arrival);
        flight.PlannedServices.ShouldHaveSingleItem().Service.ServiceId.ShouldBe(serviceId);
        flight.AssignedEmployees.ShouldHaveSingleItem().Employee.StaffMemberId.ShouldBe(staff.StaffMemberId);
        db.ChangeTracker.HasChanges().ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Scheduling_queues_one_notification_per_distinct_employee_and_groups_multiple_flights(int batchDates)
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader();
        var employeeIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        foreach (var id in employeeIds)
            reader.StaffMembers.Add(id, new(id, "Employee", "E1", reader.StationId, Guid.NewGuid(), true));
        var user = new TestUser();
        var arrival = new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero);
        var selectedStaff = new[] { employeeIds[0], employeeIds[1], employeeIds[0] };

        if (batchDates == 0)
        {
            var result = await new ScheduleFlightCommandHandler(db, AdminScope(), new MasterDataResolver(reader),
                user, new AuditActor(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
                .Handle(new ScheduleFlightCommand(Guid.NewGuid(), reader.StationId, Guid.NewGuid(), "123",
                    arrival, arrival.AddHours(1), null, [reader.ServiceId], selectedStaff), CancellationToken.None);
            result.IsSuccess.ShouldBeTrue();
        }
        else
        {
            var dates = Enumerable.Range(0, batchDates).Select(day => new DateOnly(2026, 11, 10).AddDays(day)).ToList();
            dates.Add(dates[0]);
            var result = await new ScheduleFlightsCommandHandler(db, AdminScope(), new MasterDataResolver(reader),
                user, new AuditActor(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
                .Handle(new ScheduleFlightsCommand(Guid.NewGuid(), reader.StationId, Guid.NewGuid(), "123",
                    new TimeOnly(10, 0), new TimeOnly(11, 0), dates, null, [reader.ServiceId], selectedStaff), CancellationToken.None);
            result.IsSuccess.ShouldBeTrue();
        }

        var envelopes = await db.OutboxMessages.ToListAsync();
        envelopes.Count.ShouldBe(2);
        (await db.Flights.CountAsync()).ShouldBe(Math.Max(1, batchDates));
        if (batchDates > 1)
        {
            envelopes.ShouldAllBe(envelope => envelope.Type.Contains(nameof(FlightScheduleUpdated)));
            var events = envelopes.Select(envelope => JsonSerializer.Deserialize<FlightScheduleUpdated>(envelope.Content)!).ToList();
            events.Select(item => item.StaffMemberId).ShouldBe(employeeIds, ignoreOrder: true);
            events.ShouldAllBe(item => item.FlightCount == batchDates && item.UpdatedByUserId == user.UserId);
        }
        else
        {
            envelopes.ShouldAllBe(envelope => envelope.Type.Contains(nameof(FlightEmployeeAssigned)));
            var events = envelopes.Select(envelope => JsonSerializer.Deserialize<FlightEmployeeAssigned>(envelope.Content)!).ToList();
            var flightId = (await db.Flights.SingleAsync()).Id;
            events.Select(item => item.StaffMemberId).ShouldBe(employeeIds, ignoreOrder: true);
            events.ShouldAllBe(item => item.FlightId == flightId && item.Source == FlightAssignmentSource.Roster);
            events.ShouldAllBe(item => item.AssignedByUserId == user.UserId && item.AssignedByDisplayName == "Scheduler");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Planning_schedule_edits_notify_retained_assignees_once_without_duplicating_new_assignments(bool changeTimes)
    {
        await using var db = NewDb(); var reader = new SchedulingMasterDataReader();
        var retained = new StaffMemberSnapshot(Guid.NewGuid(), "Retained", "E1");
        var removed = new StaffMemberSnapshot(Guid.NewGuid(), "Removed", "E2");
        var added = new StaffMemberSnapshot(Guid.NewGuid(), "Added", "E3");
        foreach (var employee in new[] { retained, removed, added })
            reader.StaffMembers.Add(employee.StaffMemberId, new(employee.StaffMemberId, employee.FullName,
                employee.EmployeeId, reader.StationId, Guid.NewGuid(), true));
        var flight = NewFlight(reader.StationId, [retained, removed]); db.Flights.Add(flight); await db.SaveChangesAsync();
        var user = new TestUser();
        var handler = new UpdateScheduledFlightCommandHandler(db, AdminScope(), new MasterDataResolver(reader),
            user, new AuditActor(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance);
        var shift = changeTimes ? TimeSpan.FromDays(1) : TimeSpan.Zero;
        var command = new UpdateScheduledFlightCommand(flight.Id, flight.Customer.CustomerId, reader.StationId,
            flight.OperationType.OperationTypeId, flight.Schedule.Sta + shift, flight.Schedule.Std + shift, null,
            [reader.ServiceId], flight.RowVersion, [retained.StaffMemberId, added.StaffMemberId, added.StaffMemberId]);

        (await handler.Handle(command, default)).IsSuccess.ShouldBeTrue();
        (await handler.Handle(command, default)).IsSuccess.ShouldBeTrue();

        var rows = await db.OutboxMessages.ToListAsync();
        var assignments = rows.Where(row => row.Type.Contains(nameof(FlightEmployeeAssigned)))
            .Select(row => JsonSerializer.Deserialize<FlightEmployeeAssigned>(row.Content)!).ToList();
        assignments.ShouldHaveSingleItem().StaffMemberId.ShouldBe(added.StaffMemberId);
        var updates = rows.Where(row => row.Type.Contains(nameof(FlightScheduleUpdated)))
            .Select(row => JsonSerializer.Deserialize<FlightScheduleUpdated>(row.Content)!).ToList();
        if (changeTimes)
        {
            var update = updates.ShouldHaveSingleItem(); update.StaffMemberId.ShouldBe(retained.StaffMemberId);
            update.FlightId.ShouldBe(flight.Id); update.FlightNumber.ShouldBe(flight.FlightNumber.Value);
            update.FlightCount.ShouldBe(1); update.UpdatedByUserId.ShouldBe(user.UserId);
        }
        else updates.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Roster_changes_notify_only_new_employees_and_leave_repeats_and_removals_silent(bool throughPlanningEdit)
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader();
        var existing = new StaffMemberSnapshot(Guid.NewGuid(), "Existing", "E1");
        var added = new StaffMemberSnapshot(Guid.NewGuid(), "Added", "E2");
        foreach (var employee in new[] { existing, added })
            reader.StaffMembers.Add(employee.StaffMemberId, new(employee.StaffMemberId, employee.FullName,
                employee.EmployeeId, reader.StationId, Guid.NewGuid(), true));
        var flight = NewFlight(reader.StationId, [existing]);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();

        async Task ChangeRoster(IReadOnlyList<Guid> staffIds)
        {
            if (throughPlanningEdit)
            {
                var result = await new UpdateScheduledFlightCommandHandler(db, AdminScope(), new MasterDataResolver(reader),
                    new TestUser(), new AuditActor(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
                    .Handle(new UpdateScheduledFlightCommand(flight.Id, flight.Customer.CustomerId, reader.StationId,
                        flight.OperationType.OperationTypeId, flight.Schedule.Sta, flight.Schedule.Std, null,
                        [reader.ServiceId], flight.RowVersion, staffIds), CancellationToken.None);
                result.IsSuccess.ShouldBeTrue();
            }
            else
            {
                var result = await new AssignEmployeesCommandHandler(db, AdminScope(), new MasterDataResolver(reader),
                    new TestUser(), new AuditActor(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
                    .Handle(new AssignEmployeesCommand(flight.Id, staffIds, flight.RowVersion), CancellationToken.None);
                result.IsSuccess.ShouldBeTrue();
            }
        }

        await ChangeRoster([existing.StaffMemberId, added.StaffMemberId, added.StaffMemberId]);
        await ChangeRoster([existing.StaffMemberId, added.StaffMemberId]);
        await ChangeRoster([]);

        var envelope = await db.OutboxMessages.SingleAsync();
        var assignment = JsonSerializer.Deserialize<FlightEmployeeAssigned>(envelope.Content)!;
        assignment.StaffMemberId.ShouldBe(added.StaffMemberId);
        assignment.Source.ShouldBe(FlightAssignmentSource.Roster);
        assignment.AssignedByDisplayName.ShouldBe("Scheduler");
        flight.AssignedEmployees.ShouldBeEmpty();
    }

    private static Flight NewFlight(Guid stationId, IReadOnlyList<StaffMemberSnapshot> staff, bool perLanding = false,
        string stationIata = "RUH", int arrivalDay = 1, AircraftTypeSnapshot? aircraftType = null, string? customerCode = "SV",
        Guid? operationTypeId = null) =>
        Flight.ScheduleNew(new CustomerSnapshot(Guid.NewGuid(), customerCode, "Saudia"),
            new StationSnapshot(stationId, stationIata, "Station"), new OperationTypeSnapshot(operationTypeId ?? Guid.NewGuid(), "Transit"),
            FlightNumber.Create("123", customerCode).Value, ScheduledTime.Create(new DateTimeOffset(2026, 7, arrivalDay, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, arrivalDay, 10, 0, 0, TimeSpan.Zero)).Value,
            aircraftType, [new ServiceSnapshot(perLanding ? WellKnownMasterDataIds.AircraftPerLandingService : Guid.NewGuid(), "Service")],
            staff, Guid.NewGuid(), DateTimeOffset.UtcNow).Value;

    private static OperationsDbContext NewDb() => new(new DbContextOptionsBuilder<OperationsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static StaticScope AdminScope() => new(new OperationsScopeContext(UserType.SystemAdministrator, null, null));

    private sealed class StaticScope(OperationsScopeContext context) : IOperationsScope
    {
        public Task<Result<OperationsScopeContext>> ResolveAsync(CancellationToken cancellationToken) => Task.FromResult(Result.Success(context));
    }

    private sealed class TestUser(bool canAssign = true) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => canAssign && permission == OperationsPermissions.Flights.Assign;
    }

    private sealed class AuditActor : IAuditContext
    {
        public Guid? ActorId => null;
        public string? ActorDisplayName => "Scheduler";
        public bool IsSystemActor => false;
        public string? CorrelationId => null;
    }
}
