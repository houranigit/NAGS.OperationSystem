using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Authorization;
using Operations.Application.Features.WorkOrders;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class WorkOrderQueryOrderingTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Default_order_uses_latest_update_or_creation_descending_with_stable_ties()
    {
        await using var db = new OperationsDbContext(
            new DbContextOptionsBuilder<OperationsDbContext>()
                .UseInMemoryDatabase($"work-order-order-{Guid.NewGuid()}")
                .Options);

        var recentlyUpdated = CreateWorkOrder("RJ101", Now.AddHours(-4), Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var newestCreated = CreateWorkOrder("RJ102", Now, Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var olderCreated = CreateWorkOrder("RJ103", Now.AddHours(-1), Guid.Parse("00000000-0000-0000-0000-000000000003"));
        recentlyUpdated.UpdateDetails(
            WorkOrderType.Completion,
            recentlyUpdated.ActualFlightNumber,
            null,
            null,
            null,
            null,
            "Most recently changed",
            [],
            [],
            Now.AddHours(1)).IsSuccess.ShouldBeTrue();

        db.WorkOrders.AddRange(recentlyUpdated, newestCreated, olderCreated);
        await db.SaveChangesAsync();

        var result = await new GetWorkOrdersQueryHandler(
            db,
            new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null)),
            new StaticUserContext())
            .Handle(new GetWorkOrdersQuery(PageSize: 20), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Select(item => item.Id).ShouldBe(
            [recentlyUpdated.Id, newestCreated.Id, olderCreated.Id]);
    }

    private static WorkOrder CreateWorkOrder(string flightNumber, DateTimeOffset createdAtUtc, Guid id)
    {
        var flight = Flight.ScheduleNew(
            new CustomerSnapshot(Guid.NewGuid(), "RJ", "Royal Jordanian"),
            new StationSnapshot(Guid.NewGuid(), "RUH", "Riyadh"),
            new OperationTypeSnapshot(Guid.NewGuid(), "Transit"),
            FlightNumber.Create(flightNumber).Value,
            ScheduledTime.Create(Now, Now.AddHours(1)).Value,
            aircraftType: null,
            plannedServices: [new ServiceSnapshot(Guid.NewGuid(), "Marshalling")],
            assignedEmployees: [],
            contractId: null,
            contractNumber: null,
            createdByUserId: Guid.NewGuid(),
            now: createdAtUtc).Value;

        return WorkOrder.SubmitNew(
            flight,
            WorkOrderType.Completion,
            Guid.NewGuid(),
            owner: null,
            actualFlightNumber: null,
            aircraftType: null,
            aircraftTailNumber: null,
            actuals: null,
            cancellation: null,
            remarks: null,
            serviceLines: [],
            tasks: [],
            now: createdAtUtc,
            id: id).Value;
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
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => true;
    }
}
