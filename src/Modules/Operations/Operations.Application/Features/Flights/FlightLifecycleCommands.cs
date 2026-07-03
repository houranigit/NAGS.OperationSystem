using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Contracts;
using Operations.Application.Features.WorkOrders;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;

namespace Operations.Application.Features.Flights;

// --- Cancel flight (creates a submitted cancellation work order) ------------

public sealed record CancelFlightCommand(Guid FlightId, DateTimeOffset CanceledAtUtc, string? Reason) : ICommand<Guid>;

public sealed class CancelFlightCommandValidator : AbstractValidator<CancelFlightCommand>
{
    public CancelFlightCommandValidator()
    {
        RuleFor(x => x.FlightId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(1000);
    }
}

public sealed class CancelFlightCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<CancelFlightCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CancelFlightCommand request, CancellationToken cancellationToken)
    {
        var flight = await db.Flights.FirstOrDefaultAsync(f => f.Id == request.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var stationCheck = scopeResult.Value.EnsureStation(flight.Station.StationId);
        if (stationCheck.IsFailure)
            return stationCheck.Error;

        if (flight.IsUpdateLocked)
            return Error.Conflict("This flight is already settled.", "Operations.Flight.Locked");

        var now = timeProvider.GetUtcNow();
        var cancellation = new CancellationDetails(user.UserId ?? Guid.Empty, request.CanceledAtUtc.ToUniversalTime(), request.Reason?.Trim());
        var workOrder = WorkOrder.OpenCancellation(WorkOrderContextFactory.From(flight), cancellation, user.UserId ?? Guid.Empty, now);

        // A cancellation has no content to author, so it is submitted straight to review.
        var submit = workOrder.Submit([], flight.IsPerLanding, now);
        if (submit.IsFailure)
            return submit.Error;

        flight.OnWorkOrderSubmitted(now);

        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync(cancellationToken);
        return workOrder.Id;
    }
}

// --- Claim a Per-Landing flight ---------------------------------------------

public sealed record ClaimPerLandingFlightCommand(Guid FlightId, byte[] RowVersion) : ICommand;

public sealed class ClaimPerLandingFlightCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    MasterDataResolver resolver,
    TimeProvider timeProvider) : ICommandHandler<ClaimPerLandingFlightCommand>
{
    public async Task<Result> Handle(ClaimPerLandingFlightCommand request, CancellationToken cancellationToken)
    {
        var flight = await db.Flights.Include(f => f.AssignedEmployees).Include(f => f.PlannedServices)
            .FirstOrDefaultAsync(f => f.Id == request.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var stationCheck = scopeResult.Value.EnsureStation(flight.Station.StationId);
        if (stationCheck.IsFailure)
            return stationCheck.Error;
        if (scopeResult.Value.StaffMemberId is not { } staffId)
            return Error.Forbidden("Only station staff can claim a flight.", "Operations.Flight.ClaimNotAllowed");

        var employee = await resolver.StaffMemberAsync(staffId, cancellationToken);
        if (employee.IsFailure)
            return employee.Error;

        var claim = flight.Claim(employee.Value, timeProvider.GetUtcNow());
        if (claim.IsFailure)
            return claim.Error;

        db.SetOriginalRowVersion(flight, request.RowVersion);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// --- Work-Order-First: create ad-hoc flight + work order --------------------

public sealed record CreateAdHocFlightWithWorkOrderCommand(
    Guid CustomerId,
    Guid OperationTypeId,
    string FlightNumber,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    Guid? AircraftTypeId,
    IReadOnlyList<Guid> PlannedServiceIds,
    bool AcknowledgeDuplicates) : ICommand<AdHocFlightResult>;

public sealed record AdHocFlightResult(Guid FlightId, Guid WorkOrderId, IReadOnlyList<DuplicateCandidateDto> DuplicateCandidates);

public sealed class CreateAdHocFlightWithWorkOrderCommandValidator : AbstractValidator<CreateAdHocFlightWithWorkOrderCommand>
{
    public CreateAdHocFlightWithWorkOrderCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.OperationTypeId).NotEmpty();
        RuleFor(x => x.FlightNumber).NotEmpty().MaximumLength(12);
    }
}

public sealed class CreateAdHocFlightWithWorkOrderCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    MasterDataResolver resolver,
    FlightDuplicateDetector duplicateDetector,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<CreateAdHocFlightWithWorkOrderCommand, AdHocFlightResult>
{
    public async Task<Result<AdHocFlightResult>> Handle(CreateAdHocFlightWithWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        if (scopeResult.Value.StationId is not { } stationId || scopeResult.Value.StaffMemberId is not { } staffId)
            return Error.Forbidden("Only station staff can create an ad-hoc flight.", "Operations.Flight.AdHocNotAllowed");

        var build = await FlightBuildHelpers.BuildAsync(resolver, request.CustomerId, stationId, request.OperationTypeId,
            request.AircraftTypeId, request.FlightNumber, request.ScheduledArrivalUtc, request.ScheduledDepartureUtc,
            request.PlannedServiceIds, cancellationToken);
        if (build.IsFailure)
            return build.Error;

        var candidates = await duplicateDetector.FindAsync(request.CustomerId, stationId, request.FlightNumber, request.ScheduledArrivalUtc, cancellationToken);
        var strong = candidates.FirstOrDefault(c => c.Score >= FlightDuplicateDetector.StrongMatchThreshold);
        if (strong is not null && !request.AcknowledgeDuplicates)
        {
            return Error.Conflict(
                "A likely duplicate flight already exists. Review the candidates and confirm to proceed or link to the existing flight.",
                "Operations.Flight.PotentialDuplicate");
        }

        var creator = await resolver.StaffMemberAsync(staffId, cancellationToken);
        if (creator.IsFailure)
            return creator.Error;

        var now = timeProvider.GetUtcNow();
        var b = build.Value;
        var flight = Flight.CreateAdHoc(b.Customer, b.Station, b.OperationType, b.FlightNumber, b.Schedule, b.AircraftType,
            b.PlannedServices, creator.Value, user.UserId ?? Guid.Empty, now);
        if (flight.IsFailure)
            return flight.Error;

        if (strong is not null)
            flight.Value.FlagPotentialDuplicate(strong.FlightId, now);

        var workOrder = WorkOrder.OpenCompletion(WorkOrderContextFactory.From(flight.Value), user.UserId ?? Guid.Empty, now);

        db.Flights.Add(flight.Value);
        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync(cancellationToken);

        return new AdHocFlightResult(flight.Value.Id, workOrder.Id, candidates);
    }
}
