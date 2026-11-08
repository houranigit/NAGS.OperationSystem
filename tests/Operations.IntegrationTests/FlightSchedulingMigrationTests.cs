using BuildingBlocks.Application.Messaging;
using Microsoft.EntityFrameworkCore;
using Operations.Contracts;
using Operations.Infrastructure.Persistence;
using Shouldly;
using Testcontainers.MsSql;

namespace Operations.IntegrationTests;

public sealed class FlightSchedulingMigrationTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    [Fact]
    public async Task Scheduling_reset_preserves_main_flights_rosters_and_planned_services()
    {
        var connectionString = _sql.GetConnectionString().Replace("Database=master", "Database=FlightSchedulingMigrationTests");
        var options = new DbContextOptionsBuilder<OperationsDbContext>()
            .UseSqlServer(connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", OperationsDbContext.Schema))
            .Options;
        await using var db = new OperationsDbContext(options);
        await db.Database.MigrateAsync("20261030192847_Operations_WorkOrderReferenceNumbers");
        var previousMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();

        var flightId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var stationId = Guid.NewGuid();
        var operationTypeId = Guid.NewGuid();
        var aircraftTypeId = Guid.NewGuid();
        var staffMemberId = Guid.NewGuid();
        var plannedServiceId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var performedLineId = Guid.NewGuid();
        var createdByUserId = Guid.NewGuid();
        var retiredEventId = Guid.NewGuid();
        var auditEventId = Guid.NewGuid();
        var processedRetiredEventId = Guid.NewGuid();
        var eventContent = "{}";
        var arrival = new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);
        var departure = arrival.AddHours(2);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [operations].[flights]
                ([Id], [CustomerId], [CustomerIataCode], [CustomerName],
                 [StationId], [StationIataCode], [StationName], [OperationTypeId], [OperationTypeName],
                 [FlightNumber], [OriginalFlightNumber], [ScheduledArrivalUtc], [ScheduledDepartureUtc],
                 [AircraftTypeId], [AircraftManufacturer], [AircraftModel], [Status], [CreatedByUserId], [CreatedAtUtc])
            VALUES
                ({flightId}, {customerId}, N'BA', N'Baseline Airline',
                 {stationId}, N'DMM', N'Dammam', {operationTypeId}, N'Transit',
                 N'BASE100', N'OLD100', {arrival}, {departure},
                 {aircraftTypeId}, N'Airbus', N'A320', 0, {createdByUserId}, {arrival});

            INSERT INTO [operations].[flight_assigned_employees]
                ([Id], [FlightId], [StaffMemberId], [StaffFullName], [StaffEmployeeId])
            VALUES ({assignmentId}, {flightId}, {staffMemberId}, N'Assigned Employee', N'EMP-100');

            INSERT INTO [operations].[flight_planned_services]
                ([Id], [FlightId], [ServiceId], [ServiceName])
            VALUES ({plannedServiceId}, {flightId}, {serviceId}, N'Planned Marshalling');

            INSERT INTO [operations].[work_orders]
                ([Id], [FlightId], [Type], [Status], [IsMergeGenerated], [OwnerUserId],
                 [CustomerId], [CustomerIataCode], [CustomerName], [StationId], [StationIataCode], [StationName],
                 [OperationTypeId], [OperationTypeName], [PlannedFlightNumber],
                 [ScheduledArrivalUtc], [ScheduledDepartureUtc], [ActualFlightNumber], [CreatedAtUtc], [LastReturnToRampSequence])
            SELECT {workOrderId}, [Id], 0, 0, 0, [CreatedByUserId],
                   [CustomerId], [CustomerIataCode], [CustomerName], [StationId], [StationIataCode], [StationName],
                   [OperationTypeId], [OperationTypeName], [FlightNumber],
                   [ScheduledArrivalUtc], [ScheduledDepartureUtc], N'', [CreatedAtUtc], 0
            FROM [operations].[flights] WHERE [Id] = {flightId};

            INSERT INTO [operations].[work_order_service_lines]
                ([Id], [WorkOrderId], [ServiceId], [ServiceName], [FromUtc], [ToUtc])
            VALUES ({performedLineId}, {workOrderId}, {Guid.NewGuid()}, N'Actual extra work', {arrival}, {departure});

            INSERT INTO [operations].[outbox_messages]
                ([Id], [OccurredOnUtc], [Type], [Content], [ProcessedOnUtc], [Attempts])
            VALUES ({retiredEventId}, {arrival}, N'Operations.Contracts.WorkOrderFileDeletionRequested, Operations.Contracts', {eventContent}, NULL, 0),
                   ({auditEventId}, {arrival}, N'BuildingBlocks.Contracts.Auditing.AuditEntryRecorded, BuildingBlocks.Contracts', {eventContent}, NULL, 0),
                   ({processedRetiredEventId}, {arrival}, N'Operations.Contracts.WorkOrderFileDeletionRequested, Operations.Contracts', {eventContent}, {arrival}, 1);
            """);

        var pendingInvitation = OutboxMessage.Create(new FlightEmployeeAssigned
        {
            FlightId = flightId,
            FlightNumber = "BASE100",
            StaffMemberId = staffMemberId,
            Source = FlightAssignmentSource.Invite,
            OccurredOnUtc = arrival
        });
        db.OutboxMessages.Add(pendingInvitation);
        await db.SaveChangesAsync();

        await db.Database.MigrateAsync("20261103214037_Operations_FlightSchedulingBaseline");
        var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        appliedMigrations.Length.ShouldBe(previousMigrations.Length + 1);
        appliedMigrations.Take(previousMigrations.Length).ShouldBe(previousMigrations);
        appliedMigrations.Last().ShouldEndWith("_Operations_FlightSchedulingBaseline");

        var flight = await db.Flights.AsNoTracking()
            .Include(item => item.PlannedServices)
            .Include(item => item.AssignedEmployees)
            .SingleAsync();
        flight.Id.ShouldBe(flightId);
        flight.FlightNumber.Value.ShouldBe("BASE100");
        flight.Customer.CustomerId.ShouldBe(customerId);
        flight.Customer.Name.ShouldBe("Baseline Airline");
        flight.Station.StationId.ShouldBe(stationId);
        flight.Station.IataCode.ShouldBe("DMM");
        flight.OperationType.OperationTypeId.ShouldBe(operationTypeId);
        flight.AircraftType!.AircraftTypeId.ShouldBe(aircraftTypeId);
        flight.Schedule.Sta.ShouldBe(arrival);
        flight.Schedule.Std.ShouldBe(departure);
        flight.CreatedByUserId.ShouldBe(createdByUserId);
        flight.CreatedAtUtc.ShouldBe(arrival);
        var planned = flight.PlannedServices.ShouldHaveSingleItem();
        planned.Id.ShouldBe(plannedServiceId);
        planned.Service.ServiceId.ShouldBe(serviceId);
        planned.Service.Name.ShouldBe("Planned Marshalling");
        var employee = flight.AssignedEmployees.ShouldHaveSingleItem();
        employee.Id.ShouldBe(assignmentId);
        employee.Employee.StaffMemberId.ShouldBe(staffMemberId);
        employee.Employee.FullName.ShouldBe("Assigned Employee");
        employee.Employee.EmployeeId.ShouldBe("EMP-100");
        (await db.OutboxMessages.AnyAsync(message => message.Id == retiredEventId)).ShouldBeFalse();
        (await db.OutboxMessages.SingleAsync(message => message.Id == auditEventId)).ProcessedOnUtc.ShouldBeNull();
        (await db.OutboxMessages.SingleAsync(message => message.Id == pendingInvitation.Id)).ProcessedOnUtc.ShouldBeNull();
        (await db.OutboxMessages.SingleAsync(message => message.Id == processedRetiredEventId))
            .ProcessedOnUtc.ShouldBe(arrival);

        (await ScalarIntAsync(db, """
            SELECT COUNT(*) FROM sys.tables
            WHERE schema_id = SCHEMA_ID('operations')
              AND (name LIKE 'work_order_%' OR name IN
                ('flight_timeline_entries', 'flight_reminder_schedules', 'Operations_MobileMutations'))
            """)).ShouldBe(0);
        (await ScalarIntAsync(db, """
            SELECT COUNT(*) FROM sys.columns
            WHERE object_id = OBJECT_ID('operations.flights')
              AND name IN ('Status', 'OriginalFlightNumber', 'ContractId', 'ContractNumber',
                'MergedIntoFlightId')
            """)).ShouldBe(0);
    }

    public async Task InitializeAsync() => await _sql.StartAsync();

    public async Task DisposeAsync() => await _sql.DisposeAsync();

    private static async Task<int> ScalarIntAsync(OperationsDbContext db, string sql)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
