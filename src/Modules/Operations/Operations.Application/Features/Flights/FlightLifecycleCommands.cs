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
using Operations.Domain.Enumerations;
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
    MasterDataResolver resolver,
    IFlightTimelineWriter timeline,
    IWorkOrderTimelineWriter workOrderTimeline,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<CancelFlightCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CancelFlightCommand request, CancellationToken cancellationToken)
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
            return Error.Conflict("This flight is already settled.", "Operations.Flight.Locked");

        StaffMemberSnapshot? owner = null;
        if (scopeResult.Value.StaffMemberId is { } staffId)
        {
            var staff = await resolver.StaffMemberAsync(staffId, cancellationToken);
            if (staff.IsFailure)
                return staff.Error;
            owner = staff.Value;

            var hasOwnWorkOrder = await db.WorkOrders.AnyAsync(w => w.FlightId == flight.Id &&
                w.OwnerStaffMemberId == staffId, cancellationToken);
            if (hasOwnWorkOrder)
                return Error.Conflict("You already have a work order for this flight.", "Operations.WorkOrder.AlreadyOpen");
        }

        var now = timeProvider.GetUtcNow();
        var cancellation = new CancellationDetails(user.UserId ?? Guid.Empty, request.CanceledAtUtc.ToUniversalTime(), request.Reason?.Trim());
        var workOrder = WorkOrder.OpenCancellation(WorkOrderContextFactory.From(flight), cancellation, user.UserId ?? Guid.Empty, owner, now);
        var submit = workOrder.Submit(now);
        if (submit.IsFailure)
            return submit.Error;

        flight.OnWorkOrderSubmitted(now);

        db.WorkOrders.Add(workOrder);
        await timeline.AppendAsync(flight.Id, FlightTimelineEventType.WorkOrderSubmitted, now, workOrder.Id, cancellationToken: cancellationToken);
        await workOrderTimeline.AppendAsync(workOrder, WorkOrderTimelineEventType.Submitted, now, cancellationToken: cancellationToken);
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
    IFlightTimelineWriter timeline,
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

        var now = timeProvider.GetUtcNow();
        var alreadyAssigned = flight.AssignedEmployees.Any(e => e.Employee.StaffMemberId == staffId);
        db.SetOriginalRowVersion(flight, request.RowVersion);
        var claim = flight.Claim(employee.Value, now);
        if (claim.IsFailure)
            return claim.Error;

        if (!alreadyAssigned)
            await timeline.AppendAsync(flight.Id, FlightTimelineEventType.EmployeeAssigned, now, details: employee.Value.FullName, cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// --- Work-Order-First: create ad-hoc flight + work order --------------------
// Collects both flight planning fields and work-order actual fields; the work order is created as a
// Submitted record owned by the creating staff member and follows the normal review/approve flow.

public sealed record CreateAdHocFlightWithWorkOrderCommand(
    Guid CustomerId,
    Guid OperationTypeId,
    string FlightNumber,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    Guid? AircraftTypeId,
    IReadOnlyList<Guid> PlannedServiceIds,
    bool AcknowledgeDuplicates,
    bool IsCancellation,
    DateTimeOffset? CancellationAtUtc,
    string? CancellationReason,
    string? ActualFlightNumber,
    Guid? ActualAircraftTypeId,
    string? AircraftTailNumber,
    DateTimeOffset? ActualArrivalUtc,
    DateTimeOffset? ActualDepartureUtc,
    IReadOnlyList<ServiceLineRequest> ServiceLines,
    IReadOnlyList<TaskRequest> Tasks,
    string? Remarks,
    string? CustomerSignatureReference) : ICommand<AdHocFlightResult>;

public sealed record AdHocFlightResult(Guid FlightId, Guid WorkOrderId, IReadOnlyList<DuplicateCandidateDto> DuplicateCandidates);

public sealed class CreateAdHocFlightWithWorkOrderCommandValidator : AbstractValidator<CreateAdHocFlightWithWorkOrderCommand>
{
    public CreateAdHocFlightWithWorkOrderCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.OperationTypeId).NotEmpty();
        RuleFor(x => x.FlightNumber).NotEmpty().MaximumLength(12);
        RuleFor(x => x.PlannedServiceIds).NotEmpty().When(x => !x.IsCancellation);
        RuleFor(x => x.CancellationAtUtc).NotNull().When(x => x.IsCancellation);
        RuleFor(x => x.CancellationReason).MaximumLength(1000);
        RuleFor(x => x.Remarks).MaximumLength(2000);
    }
}

public sealed class CreateAdHocFlightWithWorkOrderCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    MasterDataResolver resolver,
    WorkOrderInputBuilder builder,
    FlightDuplicateDetector duplicateDetector,
    IFlightTimelineWriter timeline,
    IWorkOrderTimelineWriter workOrderTimeline,
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

        var candidates = await duplicateDetector.FindAsync(
            request.CustomerId,
            stationId,
            request.ScheduledArrivalUtc,
            request.ScheduledDepartureUtc,
            excludeFlightId: null,
            cancellationToken);
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
            b.PlannedServices, creator.Value, user.UserId ?? Guid.Empty, now,
            allowEmptyPlannedServices: request.IsCancellation);
        if (flight.IsFailure)
            return flight.Error;

        if (strong is not null)
            flight.Value.FlagPotentialDuplicate(strong.FlightId, now);

        var context = WorkOrderContextFactory.From(flight.Value);
        WorkOrder workOrder;
        if (request.IsCancellation)
        {
            var cancellation = new CancellationDetails(
                user.UserId ?? Guid.Empty, request.CancellationAtUtc!.Value.ToUniversalTime(), request.CancellationReason?.Trim());
            workOrder = WorkOrder.OpenCancellation(context, cancellation, user.UserId ?? Guid.Empty, creator.Value, now);
        }
        else
        {
            workOrder = WorkOrder.OpenCompletion(context, user.UserId ?? Guid.Empty, creator.Value, now);
        }

        var applyActuals = await ApplyWorkOrderFieldsAsync(workOrder, request, now, cancellationToken);
        if (applyActuals.IsFailure)
            return applyActuals.Error;

        var submit = workOrder.Submit(now);
        if (submit.IsFailure)
            return submit.Error;

        flight.Value.OnWorkOrderSubmitted(now);

        db.Flights.Add(flight.Value);
        db.WorkOrders.Add(workOrder);
        await timeline.AppendAsync(flight.Value.Id, FlightTimelineEventType.AdHocFlightCreated, now, cancellationToken: cancellationToken);
        await timeline.AppendAsync(flight.Value.Id, FlightTimelineEventType.WorkOrderSubmitted, now, workOrder.Id, cancellationToken: cancellationToken);
        await workOrderTimeline.AppendAsync(workOrder, WorkOrderTimelineEventType.Submitted, now, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new AdHocFlightResult(flight.Value.Id, workOrder.Id, candidates);
    }

    private async Task<Result> ApplyWorkOrderFieldsAsync(
        WorkOrder workOrder, CreateAdHocFlightWithWorkOrderCommand request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.ActualFlightNumber))
        {
            var number = FlightNumber.Create(request.ActualFlightNumber);
            if (number.IsFailure)
                return number.Error;
            var set = workOrder.SetActualFlightNumber(number.Value, now);
            if (set.IsFailure)
                return set.Error;
        }

        if (request.ActualAircraftTypeId is { } actualAircraftTypeId && actualAircraftTypeId != request.AircraftTypeId)
        {
            var aircraft = await resolver.AircraftTypeAsync(actualAircraftTypeId, cancellationToken);
            if (aircraft.IsFailure)
                return aircraft.Error;
            var set = workOrder.SetActualAircraftType(aircraft.Value, now);
            if (set.IsFailure)
                return set.Error;
        }

        if (request.ActualArrivalUtc is { } ata && request.ActualDepartureUtc is { } atd)
        {
            var actuals = ActualTime.Create(ata, atd);
            if (actuals.IsFailure)
                return actuals.Error;
            var set = workOrder.SetActualTimes(actuals.Value, now);
            if (set.IsFailure)
                return set.Error;
        }

        if (request.ServiceLines.Count > 0)
        {
            var lines = await builder.BuildServiceLinesAsync(request.ServiceLines, cancellationToken);
            if (lines.IsFailure)
                return lines.Error;
            var replace = workOrder.ReplaceServiceLines(lines.Value, now);
            if (replace.IsFailure)
                return replace.Error;
        }

        if (request.Tasks.Count > 0)
        {
            var tasks = await builder.BuildTasksAsync(request.Tasks, cancellationToken);
            if (tasks.IsFailure)
                return tasks.Error;
            var replace = workOrder.ReplaceTasks(tasks.Value, now);
            if (replace.IsFailure)
                return replace.Error;
        }

        workOrder.SetAircraftTailNumber(request.AircraftTailNumber, now);
        workOrder.SetRemarks(request.Remarks, now);
        workOrder.SetCustomerSignature(request.CustomerSignatureReference, now);
        return Result.Success();
    }
}
