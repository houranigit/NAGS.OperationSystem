using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Features.Flights;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class FlightCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AssignEmployeesCommand_RejectsInProgressFlight()
    {
        await using var db = NewDb();
        var flight = CreateScheduledFlight();
        flight.OnWorkOrderSubmitted(Now);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();

        var handler = new AssignEmployeesCommandHandler(
            db,
            new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null)),
            new MasterDataResolver(new ThrowingMasterDataReader()),
            new NoopTimelineWriter(),
            TimeProvider.System);

        var result = await handler.Handle(
            new AssignEmployeesCommand(flight.Id, [Guid.NewGuid()], [1]),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.Flight.NotEditable");
    }

    private static OperationsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OperationsDbContext>()
            .UseInMemoryDatabase($"ops-{Guid.NewGuid()}")
            .Options);

    private static Flight CreateScheduledFlight() =>
        Flight.ScheduleNew(
            new CustomerSnapshot(Guid.NewGuid(), "SV", "Saudia"),
            new StationSnapshot(Guid.NewGuid(), "RUH", "Riyadh"),
            new OperationTypeSnapshot(Guid.NewGuid(), "Transit"),
            FlightNumber.Create("SV1020").Value,
            ScheduledTime.Create(Now, Now.AddHours(1)).Value,
            aircraftType: null,
            plannedServices: [new ServiceSnapshot(Guid.NewGuid(), "Marshalling")],
            assignedEmployees: [],
            contractId: null,
            contractNumber: null,
            createdByUserId: Guid.NewGuid(),
            now: Now).Value;

    private sealed class StaticScope(OperationsScopeContext context) : IOperationsScope
    {
        public Task<Result<OperationsScopeContext>> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(context));
    }

    private sealed class NoopTimelineWriter : IFlightTimelineWriter
    {
        public Task AppendAsync(
            Guid flightId,
            FlightTimelineEventType eventType,
            DateTimeOffset occurredAtUtc,
            Guid? workOrderId = null,
            string? workOrderNumber = null,
            string? details = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ThrowingMasterDataReader : IMasterDataReader
    {
        public Task<CustomerReadSnapshot?> GetCustomerAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<StationReadSnapshot?> GetStationAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<OperationTypeReadSnapshot?> GetOperationTypeAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<AircraftTypeReadSnapshot?> GetAircraftTypeAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<ServiceReadSnapshot?> GetServiceAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<ServiceReadSnapshot>> GetServicesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<StaffMemberReadSnapshot?> GetStaffMemberAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<StaffMemberReadSnapshot>> GetStaffMembersAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<StaffMemberReadSnapshot>> GetActiveStaffMembersForStationAsync(Guid stationId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<ToolReadSnapshot?> GetToolAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<MaterialReadSnapshot?> GetMaterialAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<GeneralSupportReadSnapshot?> GetGeneralSupportAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}
