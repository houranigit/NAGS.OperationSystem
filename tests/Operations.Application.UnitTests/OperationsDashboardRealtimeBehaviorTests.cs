using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Operations.Application.Abstractions;
using Operations.Application.Behaviors;
using Operations.Application.Contracts;
using Operations.Application.Features.Dashboard;
using Operations.Application.Features.Flights;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class OperationsDashboardRealtimeBehaviorTests
{
    [Fact]
    public async Task Successful_operations_command_publishes_one_invalidation()
    {
        var notifier = new RecordingNotifier();
        var behavior = new OperationsDashboardRealtimeBehavior<ScheduleFlightCommand, Result<Guid>>(
            notifier,
            NullLogger<OperationsDashboardRealtimeBehavior<ScheduleFlightCommand, Result<Guid>>>.Instance);
        var command = new ScheduleFlightCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "RT100",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1),
            AircraftTypeId: null,
            PlannedServiceIds: [Guid.NewGuid()],
            AssignedStaffMemberIds: []);

        var expectedId = Guid.NewGuid();
        var result = await behavior.Handle(
            command,
            () => Task.FromResult(Result.Success(expectedId)),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(expectedId);
        notifier.NotificationCount.ShouldBe(1);
    }

    [Fact]
    public async Task Failed_operations_command_does_not_publish()
    {
        var notifier = new RecordingNotifier();
        var behavior = new OperationsDashboardRealtimeBehavior<ClaimPerLandingFlightCommand, Result>(
            notifier,
            NullLogger<OperationsDashboardRealtimeBehavior<ClaimPerLandingFlightCommand, Result>>.Instance);

        var result = await behavior.Handle(
            new ClaimPerLandingFlightCommand(Guid.NewGuid(), [1]),
            () => Task.FromResult(Result.Failure(Error.Conflict("Conflict", "Operations.Test.Conflict"))),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        notifier.NotificationCount.ShouldBe(0);
    }

    [Fact]
    public async Task Operations_query_does_not_publish()
    {
        var notifier = new RecordingNotifier();
        var behavior = new OperationsDashboardRealtimeBehavior<GetOperationsDashboardQuery, Result<OperationsDashboardDto>>(
            notifier,
            NullLogger<OperationsDashboardRealtimeBehavior<GetOperationsDashboardQuery, Result<OperationsDashboardDto>>>.Instance);

        var result = await behavior.Handle(
            new GetOperationsDashboardQuery(),
            () => Task.FromResult(Result.Success<OperationsDashboardDto>(null!)),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        notifier.NotificationCount.ShouldBe(0);
    }

    [Fact]
    public async Task Command_from_another_application_assembly_does_not_publish()
    {
        var notifier = new RecordingNotifier();
        var behavior = new OperationsDashboardRealtimeBehavior<ForeignCommand, Result>(
            notifier,
            NullLogger<OperationsDashboardRealtimeBehavior<ForeignCommand, Result>>.Instance);

        var result = await behavior.Handle(
            new ForeignCommand(),
            () => Task.FromResult(Result.Success()),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        notifier.NotificationCount.ShouldBe(0);
    }

    [Fact]
    public async Task Realtime_failure_does_not_fail_committed_command()
    {
        var notifier = new RecordingNotifier { Exception = new InvalidOperationException("Hub unavailable") };
        var behavior = new OperationsDashboardRealtimeBehavior<ClaimPerLandingFlightCommand, Result>(
            notifier,
            NullLogger<OperationsDashboardRealtimeBehavior<ClaimPerLandingFlightCommand, Result>>.Instance);

        var result = await behavior.Handle(
            new ClaimPerLandingFlightCommand(Guid.NewGuid(), [1]),
            () => Task.FromResult(Result.Success()),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        notifier.NotificationCount.ShouldBe(1);
    }

    private sealed class RecordingNotifier : IOperationsDashboardRealtimeNotifier
    {
        public int NotificationCount { get; private set; }
        public Exception? Exception { get; init; }

        public Task NotifyChangedAsync(CancellationToken cancellationToken = default)
        {
            NotificationCount++;
            return Exception is null ? Task.CompletedTask : Task.FromException(Exception);
        }
    }

    private sealed record ForeignCommand : ICommand;
}
