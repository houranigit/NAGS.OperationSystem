using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;

namespace Operations.Application.Features.WorkOrders;

internal static class WorkOrderContextFactory
{
    public static FlightContext From(Flight flight) =>
        new(flight.Id, flight.Customer, flight.Station, flight.OperationType, flight.FlightNumber, flight.Schedule, flight.AircraftType);
}

internal static class WorkOrderOwnership
{
    /// <summary>
    /// Ensures the caller may author (edit/submit) <paramref name="workOrder"/>: administrators may;
    /// a station staff member only when they own it. Staff can never touch another employee's work order.
    /// </summary>
    public static Result EnsureCanAuthor(OperationsScopeContext scope, WorkOrder workOrder)
    {
        if (scope.IsAdministrator)
            return Result.Success();

        if (scope.StaffMemberId is { } staffId && workOrder.IsOwnedBy(staffId))
            return Result.Success();

        return Error.Forbidden("Only the work order's owner can modify it.", "Operations.WorkOrder.NotOwner");
    }
}

// --- Open work order (completion) ------------------------------------------

public sealed record OpenWorkOrderCommand(Guid FlightId) : ICommand<Guid>;

public sealed class OpenWorkOrderCommandValidator : AbstractValidator<OpenWorkOrderCommand>
{
    public OpenWorkOrderCommandValidator() => RuleFor(x => x.FlightId).NotEmpty();
}

public sealed class OpenWorkOrderCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    MasterDataResolver resolver,
    IFlightTimelineWriter timeline,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<OpenWorkOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(OpenWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var flight = await db.Flights
            .Include(f => f.PlannedServices)
            .Include(f => f.AssignedEmployees)
            .FirstOrDefaultAsync(f => f.Id == request.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var accessCheck = scopeResult.Value.EnsureFlightAccess(flight);
        if (accessCheck.IsFailure)
            return accessCheck.Error;

        if (flight.IsUpdateLocked)
            return Error.Conflict(
                "This flight is settled. Return the approved work order to review before adding another work order.",
                "Operations.Flight.Locked");

        // Multiple employees may each author their own work order for the same flight, but a single
        // staff member holds at most one active (draft/submitted) work order per flight.
        StaffMemberSnapshot? owner = null;
        if (scopeResult.Value.StaffMemberId is { } staffId)
        {
            var staff = await resolver.StaffMemberAsync(staffId, cancellationToken);
            if (staff.IsFailure)
                return staff.Error;
            owner = staff.Value;

            var hasOwnActive = await db.WorkOrders.AnyAsync(w => w.FlightId == flight.Id &&
                w.OwnerStaffMemberId == staffId &&
                (w.Status == WorkOrderStatus.Draft || w.Status == WorkOrderStatus.Submitted), cancellationToken);
            if (hasOwnActive)
                return Error.Conflict("You already have an active work order for this flight.", "Operations.WorkOrder.AlreadyOpen");
        }
        else
        {
            var hasOwnActive = await db.WorkOrders.AnyAsync(w => w.FlightId == flight.Id &&
                w.OwnerStaffMemberId == null && w.CreatedByUserId == user.UserId &&
                (w.Status == WorkOrderStatus.Draft || w.Status == WorkOrderStatus.Submitted), cancellationToken);
            if (hasOwnActive)
                return Error.Conflict("You already have an active work order for this flight.", "Operations.WorkOrder.AlreadyOpen");
        }

        // Opening a draft work order does not change the flight status; the flight moves to
        // InProgress only when a work order is submitted.
        var now = timeProvider.GetUtcNow();
        var workOrder = WorkOrder.OpenCompletion(WorkOrderContextFactory.From(flight), user.UserId ?? Guid.Empty, owner, now);

        db.WorkOrders.Add(workOrder);
        await timeline.AppendAsync(flight.Id, FlightTimelineEventType.WorkOrderCreated, now, workOrder.Id, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return workOrder.Id;
    }
}

// --- Update work order (author services/tasks/actuals) ----------------------

public sealed record UpdateWorkOrderCommand(
    Guid Id,
    IReadOnlyList<ServiceLineRequest> ServiceLines,
    IReadOnlyList<TaskRequest> Tasks,
    string? ActualFlightNumber,
    Guid? ActualAircraftTypeId,
    DateTimeOffset? ActualArrivalUtc,
    DateTimeOffset? ActualDepartureUtc,
    string? AircraftTailNumber,
    string? Remarks,
    string? CustomerSignatureReference,
    byte[] RowVersion) : ICommand;

public sealed class UpdateWorkOrderCommandValidator : AbstractValidator<UpdateWorkOrderCommand>
{
    public UpdateWorkOrderCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ActualFlightNumber).MaximumLength(12);
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class UpdateWorkOrderCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    MasterDataResolver resolver,
    WorkOrderInputBuilder builder,
    TimeProvider timeProvider) : ICommandHandler<UpdateWorkOrderCommand>
{
    public async Task<Result> Handle(UpdateWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await LoadGraph(db).FirstOrDefaultAsync(w => w.Id == request.Id, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var stationCheck = scopeResult.Value.EnsureStation(workOrder.Station.StationId);
        if (stationCheck.IsFailure)
            return stationCheck.Error;
        var ownership = WorkOrderOwnership.EnsureCanAuthor(scopeResult.Value, workOrder);
        if (ownership.IsFailure)
            return ownership.Error;

        var now = timeProvider.GetUtcNow();

        if (!string.IsNullOrWhiteSpace(request.ActualFlightNumber))
        {
            var number = FlightNumber.Create(request.ActualFlightNumber);
            if (number.IsFailure)
                return number.Error;
            var setNumber = workOrder.SetActualFlightNumber(number.Value, now);
            if (setNumber.IsFailure)
                return setNumber.Error;
        }

        if (request.ActualAircraftTypeId != workOrder.AircraftType?.AircraftTypeId)
        {
            var aircraft = await resolver.AircraftTypeAsync(request.ActualAircraftTypeId, cancellationToken);
            if (aircraft.IsFailure)
                return aircraft.Error;
            var setAircraft = workOrder.SetActualAircraftType(aircraft.Value, now);
            if (setAircraft.IsFailure)
                return setAircraft.Error;
        }

        var lines = await builder.BuildServiceLinesAsync(request.ServiceLines, cancellationToken);
        if (lines.IsFailure)
            return lines.Error;
        var replaceLines = workOrder.ReplaceServiceLines(lines.Value, now);
        if (replaceLines.IsFailure)
            return replaceLines.Error;

        var tasks = await builder.BuildTasksAsync(request.Tasks, cancellationToken);
        if (tasks.IsFailure)
            return tasks.Error;
        var replaceTasks = workOrder.ReplaceTasks(tasks.Value, now);
        if (replaceTasks.IsFailure)
            return replaceTasks.Error;

        if (request.ActualArrivalUtc is { } ata && request.ActualDepartureUtc is { } atd)
        {
            var actuals = ActualTime.Create(ata, atd);
            if (actuals.IsFailure)
                return actuals.Error;
            var setActuals = workOrder.SetActualTimes(actuals.Value, now);
            if (setActuals.IsFailure)
                return setActuals.Error;
        }

        workOrder.SetAircraftTailNumber(request.AircraftTailNumber, now);
        workOrder.SetRemarks(request.Remarks, now);
        workOrder.SetCustomerSignature(request.CustomerSignatureReference, now);

        db.SetOriginalRowVersion(workOrder, request.RowVersion);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }

        return Result.Success();
    }

    internal static IQueryable<WorkOrder> LoadGraph(IOperationsDbContext db) =>
        db.WorkOrders
            .Include(w => w.ServiceLines).ThenInclude(l => l.Employees)
            .Include(w => w.Tasks).ThenInclude(t => t.Employees)
            .Include(w => w.Tasks).ThenInclude(t => t.Tools)
            .Include(w => w.Tasks).ThenInclude(t => t.Materials)
            .Include(w => w.Tasks).ThenInclude(t => t.GeneralSupports)
            .Include(w => w.Tasks).ThenInclude(t => t.Attachments);
}

// --- Submit work order ------------------------------------------------------

public sealed record SubmitWorkOrderCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed class SubmitWorkOrderCommandValidator : AbstractValidator<SubmitWorkOrderCommand>
{
    public SubmitWorkOrderCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class SubmitWorkOrderCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IFlightTimelineWriter timeline,
    TimeProvider timeProvider) : ICommandHandler<SubmitWorkOrderCommand>
{
    public async Task<Result> Handle(SubmitWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await db.WorkOrders.FirstOrDefaultAsync(w => w.Id == request.Id, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var stationCheck = scopeResult.Value.EnsureStation(workOrder.Station.StationId);
        if (stationCheck.IsFailure)
            return stationCheck.Error;
        var ownership = WorkOrderOwnership.EnsureCanAuthor(scopeResult.Value, workOrder);
        if (ownership.IsFailure)
            return ownership.Error;

        var flight = await db.Flights.FirstOrDefaultAsync(f => f.Id == workOrder.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        // Actual services stay optional at submission; billing later compares planned vs actual.
        var now = timeProvider.GetUtcNow();
        var submit = workOrder.Submit(now);
        if (submit.IsFailure)
            return submit.Error;

        flight.OnWorkOrderSubmitted(now);
        await timeline.AppendAsync(flight.Id, FlightTimelineEventType.WorkOrderSubmitted, now, workOrder.Id, cancellationToken: cancellationToken);

        db.SetOriginalRowVersion(workOrder, request.RowVersion);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }

        return Result.Success();
    }
}
