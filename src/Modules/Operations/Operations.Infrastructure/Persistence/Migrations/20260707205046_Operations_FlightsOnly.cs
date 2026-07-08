using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_FlightsOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF SCHEMA_ID(N'operations') IS NULL
                    EXEC(N'CREATE SCHEMA [operations]');

                IF OBJECT_ID(N'[operations].[flights]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [operations].[flights] (
                        [Id] uniqueidentifier NOT NULL,
                        [CustomerId] uniqueidentifier NOT NULL,
                        [CustomerIataCode] nvarchar(3) NULL,
                        [CustomerName] nvarchar(200) NOT NULL,
                        [StationId] uniqueidentifier NOT NULL,
                        [StationIataCode] nvarchar(3) NOT NULL,
                        [StationName] nvarchar(150) NOT NULL,
                        [OperationTypeId] uniqueidentifier NOT NULL,
                        [OperationTypeName] nvarchar(100) NOT NULL,
                        [FlightNumber] nvarchar(12) NOT NULL,
                        [OriginalFlightNumber] nvarchar(12) NOT NULL,
                        [ScheduledArrivalUtc] datetimeoffset NOT NULL,
                        [ScheduledDepartureUtc] datetimeoffset NOT NULL,
                        [AircraftTypeId] uniqueidentifier NULL,
                        [AircraftManufacturer] nvarchar(100) NULL,
                        [AircraftModel] nvarchar(50) NULL,
                        [Status] int NOT NULL,
                        [ContractId] uniqueidentifier NULL,
                        [ContractNumber] nvarchar(50) NULL,
                        [MergedIntoFlightId] uniqueidentifier NULL,
                        [PotentialDuplicateOfFlightId] uniqueidentifier NULL,
                        [CreatedByUserId] uniqueidentifier NOT NULL,
                        [CreatedAtUtc] datetimeoffset NOT NULL,
                        [UpdatedAtUtc] datetimeoffset NULL,
                        [RowVersion] rowversion NOT NULL,
                        CONSTRAINT [PK_flights] PRIMARY KEY ([Id])
                    );
                END;

                IF OBJECT_ID(N'[operations].[flight_timeline_entries]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [operations].[flight_timeline_entries] (
                        [Id] uniqueidentifier NOT NULL,
                        [FlightId] uniqueidentifier NOT NULL,
                        [EventType] int NOT NULL,
                        [OccurredAtUtc] datetimeoffset NOT NULL,
                        [ActorUserId] uniqueidentifier NOT NULL,
                        [ActorName] nvarchar(200) NULL,
                        [Details] nvarchar(1000) NULL,
                        CONSTRAINT [PK_flight_timeline_entries] PRIMARY KEY ([Id])
                    );
                END;

                IF OBJECT_ID(N'[operations].[inbox_messages]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [operations].[inbox_messages] (
                        [MessageId] uniqueidentifier NOT NULL,
                        [Consumer] nvarchar(256) NOT NULL,
                        [ProcessedOnUtc] datetimeoffset NOT NULL,
                        CONSTRAINT [PK_inbox_messages] PRIMARY KEY ([MessageId], [Consumer])
                    );
                END;

                IF OBJECT_ID(N'[operations].[outbox_messages]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [operations].[outbox_messages] (
                        [Id] uniqueidentifier NOT NULL,
                        [OccurredOnUtc] datetimeoffset NOT NULL,
                        [Type] nvarchar(512) NOT NULL,
                        [Content] nvarchar(max) NOT NULL,
                        [ProcessedOnUtc] datetimeoffset NULL,
                        [Attempts] int NOT NULL,
                        [Error] nvarchar(2048) NULL,
                        CONSTRAINT [PK_outbox_messages] PRIMARY KEY ([Id])
                    );
                END;

                IF OBJECT_ID(N'[operations].[flight_assigned_employees]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [operations].[flight_assigned_employees] (
                        [Id] uniqueidentifier NOT NULL,
                        [FlightId] uniqueidentifier NOT NULL,
                        [StaffMemberId] uniqueidentifier NOT NULL,
                        [StaffFullName] nvarchar(200) NOT NULL,
                        [StaffEmployeeId] nvarchar(50) NOT NULL,
                        CONSTRAINT [PK_flight_assigned_employees] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_flight_assigned_employees_flights_FlightId] FOREIGN KEY ([FlightId]) REFERENCES [operations].[flights] ([Id]) ON DELETE CASCADE
                    );
                END;

                IF OBJECT_ID(N'[operations].[flight_planned_services]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [operations].[flight_planned_services] (
                        [Id] uniqueidentifier NOT NULL,
                        [FlightId] uniqueidentifier NOT NULL,
                        [ServiceId] uniqueidentifier NOT NULL,
                        [ServiceName] nvarchar(200) NOT NULL,
                        CONSTRAINT [PK_flight_planned_services] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_flight_planned_services_flights_FlightId] FOREIGN KEY ([FlightId]) REFERENCES [operations].[flights] ([Id]) ON DELETE CASCADE
                    );
                END;

                IF OBJECT_ID(N'[operations].[flight_timeline_entries]', N'U') IS NOT NULL
                BEGIN
                    UPDATE [operations].[flight_timeline_entries]
                    SET [EventType] = -1
                    WHERE [EventType] = 9;

                    DELETE FROM [operations].[flight_timeline_entries]
                    WHERE [EventType] NOT IN (0, -1);

                    UPDATE [operations].[flight_timeline_entries]
                    SET [EventType] = 1
                    WHERE [EventType] = -1;

                    IF COL_LENGTH(N'operations.flight_timeline_entries', N'WorkOrderId') IS NOT NULL
                        ALTER TABLE [operations].[flight_timeline_entries] DROP COLUMN [WorkOrderId];

                    IF COL_LENGTH(N'operations.flight_timeline_entries', N'WorkOrderNumber') IS NOT NULL
                        ALTER TABLE [operations].[flight_timeline_entries] DROP COLUMN [WorkOrderNumber];
                END;

                IF OBJECT_ID(N'[operations].[work_order_service_line_employees]', N'U') IS NOT NULL
                    DROP TABLE [operations].[work_order_service_line_employees];

                IF OBJECT_ID(N'[operations].[work_order_task_attachments]', N'U') IS NOT NULL
                    DROP TABLE [operations].[work_order_task_attachments];

                IF OBJECT_ID(N'[operations].[work_order_task_employees]', N'U') IS NOT NULL
                    DROP TABLE [operations].[work_order_task_employees];

                IF OBJECT_ID(N'[operations].[work_order_task_general_supports]', N'U') IS NOT NULL
                    DROP TABLE [operations].[work_order_task_general_supports];

                IF OBJECT_ID(N'[operations].[work_order_task_materials]', N'U') IS NOT NULL
                    DROP TABLE [operations].[work_order_task_materials];

                IF OBJECT_ID(N'[operations].[work_order_task_tools]', N'U') IS NOT NULL
                    DROP TABLE [operations].[work_order_task_tools];

                IF OBJECT_ID(N'[operations].[work_order_timeline_entries]', N'U') IS NOT NULL
                    DROP TABLE [operations].[work_order_timeline_entries];

                IF OBJECT_ID(N'[operations].[work_order_service_lines]', N'U') IS NOT NULL
                    DROP TABLE [operations].[work_order_service_lines];

                IF OBJECT_ID(N'[operations].[work_order_tasks]', N'U') IS NOT NULL
                    DROP TABLE [operations].[work_order_tasks];

                IF OBJECT_ID(N'[operations].[work_orders]', N'U') IS NOT NULL
                    DROP TABLE [operations].[work_orders];

                IF OBJECT_ID(N'[operations].[station_work_order_sequences]', N'U') IS NOT NULL
                    DROP TABLE [operations].[station_work_order_sequences];

                IF OBJECT_ID(N'[operations].[flights]', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'operations.flights', N'ApprovedActualAircraftManufacturer') IS NOT NULL
                        ALTER TABLE [operations].[flights] DROP COLUMN [ApprovedActualAircraftManufacturer];

                    IF COL_LENGTH(N'operations.flights', N'ApprovedActualAircraftModel') IS NOT NULL
                        ALTER TABLE [operations].[flights] DROP COLUMN [ApprovedActualAircraftModel];

                    IF COL_LENGTH(N'operations.flights', N'ApprovedActualAircraftTypeId') IS NOT NULL
                        ALTER TABLE [operations].[flights] DROP COLUMN [ApprovedActualAircraftTypeId];

                    IF COL_LENGTH(N'operations.flights', N'ApprovedActualArrivalUtc') IS NOT NULL
                        ALTER TABLE [operations].[flights] DROP COLUMN [ApprovedActualArrivalUtc];

                    IF COL_LENGTH(N'operations.flights', N'ApprovedActualDepartureUtc') IS NOT NULL
                        ALTER TABLE [operations].[flights] DROP COLUMN [ApprovedActualDepartureUtc];

                    IF COL_LENGTH(N'operations.flights', N'ApprovedActualFlightNumber') IS NOT NULL
                        ALTER TABLE [operations].[flights] DROP COLUMN [ApprovedActualFlightNumber];

                    IF COL_LENGTH(N'operations.flights', N'ApprovedAircraftTailNumber') IS NOT NULL
                        ALTER TABLE [operations].[flights] DROP COLUMN [ApprovedAircraftTailNumber];

                    IF COL_LENGTH(N'operations.flights', N'ApprovedAtUtc') IS NOT NULL
                        ALTER TABLE [operations].[flights] DROP COLUMN [ApprovedAtUtc];

                    IF COL_LENGTH(N'operations.flights', N'ApprovedByUserId') IS NOT NULL
                        ALTER TABLE [operations].[flights] DROP COLUMN [ApprovedByUserId];

                    IF COL_LENGTH(N'operations.flights', N'ApprovedCanceledAtUtc') IS NOT NULL
                        ALTER TABLE [operations].[flights] DROP COLUMN [ApprovedCanceledAtUtc];

                    IF COL_LENGTH(N'operations.flights', N'ApprovedCanceledByUserId') IS NOT NULL
                        ALTER TABLE [operations].[flights] DROP COLUMN [ApprovedCanceledByUserId];

                    IF COL_LENGTH(N'operations.flights', N'ApprovedCancellationReason') IS NOT NULL
                        ALTER TABLE [operations].[flights] DROP COLUMN [ApprovedCancellationReason];

                    IF COL_LENGTH(N'operations.flights', N'ApprovedCustomerSignatureReference') IS NOT NULL
                        ALTER TABLE [operations].[flights] DROP COLUMN [ApprovedCustomerSignatureReference];

                    IF COL_LENGTH(N'operations.flights', N'ApprovedRemarks') IS NOT NULL
                        ALTER TABLE [operations].[flights] DROP COLUMN [ApprovedRemarks];

                    IF COL_LENGTH(N'operations.flights', N'ApprovedWorkOrderId') IS NOT NULL
                        ALTER TABLE [operations].[flights] DROP COLUMN [ApprovedWorkOrderId];

                    IF COL_LENGTH(N'operations.flights', N'ApprovedWorkOrderNumber') IS NOT NULL
                        ALTER TABLE [operations].[flights] DROP COLUMN [ApprovedWorkOrderNumber];

                    IF COL_LENGTH(N'operations.flights', N'ApprovedWorkOrderType') IS NOT NULL
                        ALTER TABLE [operations].[flights] DROP COLUMN [ApprovedWorkOrderType];
                END;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_flight_assigned_employees_FlightId' AND [object_id] = OBJECT_ID(N'[operations].[flight_assigned_employees]'))
                    CREATE INDEX [IX_flight_assigned_employees_FlightId] ON [operations].[flight_assigned_employees] ([FlightId]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_flight_planned_services_FlightId' AND [object_id] = OBJECT_ID(N'[operations].[flight_planned_services]'))
                    CREATE INDEX [IX_flight_planned_services_FlightId] ON [operations].[flight_planned_services] ([FlightId]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_flight_timeline_entries_FlightId_OccurredAtUtc' AND [object_id] = OBJECT_ID(N'[operations].[flight_timeline_entries]'))
                    CREATE INDEX [IX_flight_timeline_entries_FlightId_OccurredAtUtc] ON [operations].[flight_timeline_entries] ([FlightId], [OccurredAtUtc]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_flights_OriginalFlightNumber' AND [object_id] = OBJECT_ID(N'[operations].[flights]'))
                    CREATE INDEX [IX_flights_OriginalFlightNumber] ON [operations].[flights] ([OriginalFlightNumber]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_flights_Status' AND [object_id] = OBJECT_ID(N'[operations].[flights]'))
                    CREATE INDEX [IX_flights_Status] ON [operations].[flights] ([Status]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_outbox_messages_ProcessedOnUtc' AND [object_id] = OBJECT_ID(N'[operations].[outbox_messages]'))
                    CREATE INDEX [IX_outbox_messages_ProcessedOnUtc] ON [operations].[outbox_messages] ([ProcessedOnUtc]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "flight_assigned_employees",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "flight_planned_services",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "flight_timeline_entries",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "flights",
                schema: "operations");
        }
    }
}
