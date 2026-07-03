using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
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

// --- Open work order (completion) ------------------------------------------

public sealed record OpenWorkOrderCommand(Guid FlightId) : ICommand<Guid>;

public sealed class OpenWorkOrderCommandValidator : AbstractValidator<OpenWorkOrderCommand>
{
    public OpenWorkOrderCommandValidator() => RuleFor(x => x.FlightId).NotEmpty();
}

public sealed class OpenWorkOrderCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<OpenWorkOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(OpenWorkOrderCommand request, CancellationToken cancellationToken)
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

        var hasActive = await db.WorkOrders.AnyAsync(w => w.FlightId == flight.Id &&
            (w.Status == WorkOrderStatus.Draft || w.Status == WorkOrderStatus.Submitted || w.Status == WorkOrderStatus.Approved), cancellationToken);
        if (hasActive)
            return Error.Conflict("This flight already has an active work order.", "Operations.WorkOrder.AlreadyOpen");

        var now = timeProvider.GetUtcNow();
        var workOrder = WorkOrder.OpenCompletion(WorkOrderContextFactory.From(flight), user.UserId ?? Guid.Empty, now);
        flight.OnWorkOrderOpened(now);

        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync(cancellationToken);
        return workOrder.Id;
    }
}

// --- Update work order (author services/tasks/actuals) ----------------------

public sealed record UpdateWorkOrderCommand(
    Guid Id,
    IReadOnlyList<ServiceLineRequest> ServiceLines,
    IReadOnlyList<TaskRequest> Tasks,
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
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class UpdateWorkOrderCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
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

        var now = timeProvider.GetUtcNow();

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
    TimeProvider timeProvider) : ICommandHandler<SubmitWorkOrderCommand>
{
    public async Task<Result> Handle(SubmitWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await db.WorkOrders.Include(w => w.ServiceLines).FirstOrDefaultAsync(w => w.Id == request.Id, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var stationCheck = scopeResult.Value.EnsureStation(workOrder.Station.StationId);
        if (stationCheck.IsFailure)
            return stationCheck.Error;

        var flight = await db.Flights.Include(f => f.PlannedServices).FirstOrDefaultAsync(f => f.Id == workOrder.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var requiredPlannedServiceIds = flight.PlannedServices
            .Where(p => !p.IsAircraftPerLanding)
            .Select(p => p.Service.ServiceId)
            .ToList();

        var now = timeProvider.GetUtcNow();
        var submit = workOrder.Submit(requiredPlannedServiceIds, flight.IsPerLanding, now);
        if (submit.IsFailure)
            return submit.Error;

        flight.OnWorkOrderSubmitted(now);

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
