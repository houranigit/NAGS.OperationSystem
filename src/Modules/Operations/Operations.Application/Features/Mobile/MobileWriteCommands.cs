using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Features.Flights;
using Operations.Application.Features.WorkOrders;
using Operations.Domain.Enumerations;
using Operations.Domain.Mobile;

namespace Operations.Application.Features.Mobile;

/// <summary>
/// Result of a mobile write. <see cref="Idempotent"/> is true when the request replayed a mutation
/// the server had already applied (the client retried after losing the first response).
/// </summary>
public sealed record MobileWriteResultDto(Guid WorkOrderId, Guid FlightId, bool Idempotent);

/// <summary>
/// Shared idempotency plumbing for the mobile write commands. The mutation record is added to the
/// same scoped DbContext the inner command saves through, so the business change and the
/// idempotency record commit atomically — a replayed <c>clientMutationId</c> is answered from the
/// record instead of duplicating the write.
/// </summary>
internal static class MobileMutations
{
    public static Task<MobileMutation?> FindAsync(IOperationsDbContext db, string clientMutationId, CancellationToken ct) =>
        db.MobileMutations.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ClientMutationId == clientMutationId, ct);

    public static async Task<Result<MobileWriteResultDto>> ReplayAsync(
        IOperationsDbContext db, MobileMutation mutation, CancellationToken ct)
    {
        if (mutation.WorkOrderId is { } workOrderId)
        {
            var flightId = mutation.FlightId
                ?? await db.WorkOrders.AsNoTracking()
                    .Where(w => w.Id == workOrderId)
                    .Select(w => (Guid?)w.FlightId)
                    .FirstOrDefaultAsync(ct);

            return new MobileWriteResultDto(workOrderId, flightId ?? Guid.Empty, Idempotent: true);
        }

        return new MobileWriteResultDto(Guid.Empty, mutation.FlightId ?? Guid.Empty, Idempotent: true);
    }
}

// --- Submit a work order for an existing flight ---------------------------------------

public sealed record MobileSubmitWorkOrderCommand(
    Guid FlightId,
    WorkOrderType Type,
    WorkOrderEditableCommandPayload Payload,
    string ClientMutationId) : ICommand<MobileWriteResultDto>;

public sealed class MobileSubmitWorkOrderCommandValidator : AbstractValidator<MobileSubmitWorkOrderCommand>
{
    public MobileSubmitWorkOrderCommandValidator()
    {
        RuleFor(x => x.FlightId).NotEmpty();
        RuleFor(x => x.Payload).NotNull();
        RuleFor(x => x.ClientMutationId).NotEmpty().MaximumLength(64);
    }
}

public sealed class MobileSubmitWorkOrderCommandHandler(
    IOperationsDbContext db,
    ISender sender,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<MobileSubmitWorkOrderCommand, MobileWriteResultDto>
{
    public async Task<Result<MobileWriteResultDto>> Handle(MobileSubmitWorkOrderCommand request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Forbidden("The request is not authenticated.", "Operations.WorkOrder.Unauthenticated");

        if (await MobileMutations.FindAsync(db, request.ClientMutationId, cancellationToken) is { } replay)
            return await MobileMutations.ReplayAsync(db, replay, cancellationToken);

        // Pre-generate the work order id and stage the mutation record so the inner command's
        // SaveChanges persists both atomically.
        var workOrderId = Guid.NewGuid();
        db.MobileMutations.Add(MobileMutation.Record(
            request.ClientMutationId, userId, "submit-work-order",
            workOrderId, request.FlightId, clientFlightId: null, timeProvider.GetUtcNow()));

        var result = await sender.Send(
            new SubmitWorkOrderCommand(request.FlightId, request.Type, request.Payload, request.ClientMutationId, workOrderId),
            cancellationToken);

        if (result.IsFailure)
            return result.Error;

        return new MobileWriteResultDto(result.Value, request.FlightId, Idempotent: false);
    }
}

// --- Create an ad-hoc flight + work order from scratch --------------------------------

public sealed record MobileCreateScratchWorkOrderCommand(
    Guid CustomerId,
    string FlightNumber,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    Guid? AircraftTypeId,
    IReadOnlyList<Guid> PlannedServiceIds,
    WorkOrderType Type,
    WorkOrderEditableCommandPayload Payload,
    string ClientMutationId,
    Guid ClientFlightId) : ICommand<MobileWriteResultDto>;

public sealed class MobileCreateScratchWorkOrderCommandValidator : AbstractValidator<MobileCreateScratchWorkOrderCommand>
{
    public MobileCreateScratchWorkOrderCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.FlightNumber).NotEmpty().MaximumLength(12);
        RuleFor(x => x.Payload).NotNull();
        RuleFor(x => x.ClientMutationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.ClientFlightId).NotEmpty();
    }
}

public sealed class MobileCreateScratchWorkOrderCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    ISender sender,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<MobileCreateScratchWorkOrderCommand, MobileWriteResultDto>
{
    public async Task<Result<MobileWriteResultDto>> Handle(MobileCreateScratchWorkOrderCommand request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Forbidden("The request is not authenticated.", "Operations.WorkOrder.Unauthenticated");

        if (await MobileMutations.FindAsync(db, request.ClientMutationId, cancellationToken) is { } replay)
            return await MobileMutations.ReplayAsync(db, replay, cancellationToken);

        // A different mutation already materialised this client flight (e.g. the same offline draft
        // submitted twice, or from a second device) — a duplicate scratch flight is a conflict.
        var duplicateFlight = await db.MobileMutations.AsNoTracking()
            .AnyAsync(m => m.ClientFlightId == request.ClientFlightId, cancellationToken);
        if (duplicateFlight)
            return Error.Conflict("This ad-hoc flight was already submitted.", "Operations.Mobile.ScratchFlightDuplicate");

        // The mobile client cannot pick the station; it is forced to the caller's own station.
        var scopeResult = MobileScope.EnsureStationStaff(await scope.ResolveAsync(cancellationToken));
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var stationId = scopeResult.Value.StationId!.Value;

        var flightId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        db.MobileMutations.Add(MobileMutation.Record(
            request.ClientMutationId, userId, "scratch-work-order",
            workOrderId, flightId, request.ClientFlightId, timeProvider.GetUtcNow()));

        var result = await sender.Send(
            new CreateAdHocWorkOrderCommand(
                request.CustomerId,
                stationId,
                request.FlightNumber,
                request.ScheduledArrivalUtc,
                request.ScheduledDepartureUtc,
                request.AircraftTypeId,
                request.PlannedServiceIds,
                AssignedStaffMemberIds: [],
                request.Type,
                request.Payload,
                request.ClientMutationId,
                flightId,
                workOrderId),
            cancellationToken);

        if (result.IsFailure)
            return result.Error;

        return new MobileWriteResultDto(result.Value, flightId, Idempotent: false);
    }
}

// --- Update an editable work order ------------------------------------------------------

public sealed record MobileUpdateWorkOrderCommand(
    Guid WorkOrderId,
    WorkOrderType Type,
    WorkOrderEditableCommandPayload Payload,
    string ClientMutationId) : ICommand<MobileWriteResultDto>;

public sealed class MobileUpdateWorkOrderCommandValidator : AbstractValidator<MobileUpdateWorkOrderCommand>
{
    public MobileUpdateWorkOrderCommandValidator()
    {
        RuleFor(x => x.WorkOrderId).NotEmpty();
        RuleFor(x => x.Payload).NotNull();
        RuleFor(x => x.ClientMutationId).NotEmpty().MaximumLength(64);
    }
}

public sealed class MobileUpdateWorkOrderCommandHandler(
    IOperationsDbContext db,
    ISender sender,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<MobileUpdateWorkOrderCommand, MobileWriteResultDto>
{
    public async Task<Result<MobileWriteResultDto>> Handle(MobileUpdateWorkOrderCommand request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Forbidden("The request is not authenticated.", "Operations.WorkOrder.Unauthenticated");

        if (await MobileMutations.FindAsync(db, request.ClientMutationId, cancellationToken) is { } replay)
            return await MobileMutations.ReplayAsync(db, replay, cancellationToken);

        // Offline clients cannot hold a fresh RowVersion, so the mobile surface resolves the
        // current token server-side. Ownership/editability are enforced by the inner command.
        var current = await db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == request.WorkOrderId)
            .Select(w => new { w.FlightId, w.RowVersion })
            .FirstOrDefaultAsync(cancellationToken);
        if (current is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        db.MobileMutations.Add(MobileMutation.Record(
            request.ClientMutationId, userId, "update-work-order",
            request.WorkOrderId, current.FlightId, clientFlightId: null, timeProvider.GetUtcNow()));

        var result = await sender.Send(
            new UpdateWorkOrderCommand(request.WorkOrderId, current.RowVersion, request.Type, request.Payload, request.ClientMutationId),
            cancellationToken);

        if (result.IsFailure)
            return result.Error;

        return new MobileWriteResultDto(request.WorkOrderId, current.FlightId, Idempotent: false);
    }
}

// --- Return-to-ramp: append service lines / tasks to an editable work order ----------------

public sealed record MobileReturnToRampCommand(
    Guid WorkOrderId,
    IReadOnlyList<WorkOrderServiceLineCommand> ServiceLines,
    IReadOnlyList<WorkOrderTaskCommand> Tasks,
    string ClientMutationId) : ICommand<MobileWriteResultDto>;

public sealed class MobileReturnToRampCommandValidator : AbstractValidator<MobileReturnToRampCommand>
{
    public MobileReturnToRampCommandValidator()
    {
        RuleFor(x => x.WorkOrderId).NotEmpty();
        RuleFor(x => x.ClientMutationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x)
            .Must(x => (x.ServiceLines?.Count ?? 0) + (x.Tasks?.Count ?? 0) > 0)
            .WithMessage("Return to ramp requires at least one service line or task.");
    }
}

/// <summary>
/// The legacy mobile return-to-ramp appended lines to an under-review work order. The v1 model has
/// no separate return-to-ramp record, so append semantics are implemented as a full update: the
/// current lines/tasks are re-sent (tasks keep their ids so attachments survive) with the new rows
/// appended, going through the same update pipeline and rules as any other edit.
/// </summary>
public sealed class MobileReturnToRampCommandHandler(
    IOperationsDbContext db,
    ISender sender,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<MobileReturnToRampCommand, MobileWriteResultDto>
{
    public async Task<Result<MobileWriteResultDto>> Handle(MobileReturnToRampCommand request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Forbidden("The request is not authenticated.", "Operations.WorkOrder.Unauthenticated");

        if (await MobileMutations.FindAsync(db, request.ClientMutationId, cancellationToken) is { } replay)
            return await MobileMutations.ReplayAsync(db, replay, cancellationToken);

        var workOrder = await WorkOrderLoader.ForMutation(db.WorkOrders.AsNoTracking())
            .FirstOrDefaultAsync(w => w.Id == request.WorkOrderId, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var combinedPayload = new WorkOrderEditableCommandPayload(
            workOrder.ActualFlightNumber.Value,
            workOrder.AircraftType?.AircraftTypeId,
            workOrder.AircraftTailNumber,
            workOrder.Actuals?.Ata,
            workOrder.Actuals?.Atd,
            workOrder.Cancellation?.CanceledAtUtc,
            workOrder.Cancellation?.Reason,
            workOrder.Remarks,
            workOrder.ServiceLines
                .Select(line => new WorkOrderServiceLineCommand(
                    line.Service.ServiceId,
                    line.PerformedBy.StaffMemberId,
                    line.Window.From,
                    line.Window.To,
                    line.Description))
                .Concat(request.ServiceLines ?? [])
                .ToList(),
            workOrder.Tasks
                .Select(task => new WorkOrderTaskCommand(
                    task.Id,
                    task.TaskType,
                    task.Description,
                    task.Window.From,
                    task.Window.To,
                    task.Employees.Select(e => e.Employee.StaffMemberId).ToList(),
                    task.Tools.Select(t => new WorkOrderTaskToolCommand(t.Tool.ToolId, t.Quantity.Value)).ToList(),
                    task.Materials.Select(m => new WorkOrderTaskMaterialCommand(m.Material.MaterialId, m.Quantity.Value)).ToList(),
                    task.GeneralSupports.Select(g => new WorkOrderTaskGeneralSupportCommand(g.GeneralSupport.GeneralSupportId, g.Quantity.Value)).ToList()))
                .Concat((request.Tasks ?? []).Select(task => task with { Id = null }))
                .ToList());

        db.MobileMutations.Add(MobileMutation.Record(
            request.ClientMutationId, userId, "return-to-ramp",
            workOrder.Id, workOrder.FlightId, clientFlightId: null, timeProvider.GetUtcNow()));

        var result = await sender.Send(
            new UpdateWorkOrderCommand(workOrder.Id, workOrder.RowVersion, workOrder.Type, combinedPayload, request.ClientMutationId),
            cancellationToken);

        if (result.IsFailure)
            return result.Error;

        return new MobileWriteResultDto(workOrder.Id, workOrder.FlightId, Idempotent: false);
    }
}

// --- Cancel a flight (cancellation work order) --------------------------------------------

public sealed record MobileCancelFlightCommand(
    Guid FlightId,
    DateTimeOffset CanceledAtUtc,
    string Reason,
    string ClientMutationId) : ICommand<MobileWriteResultDto>;

public sealed class MobileCancelFlightCommandValidator : AbstractValidator<MobileCancelFlightCommand>
{
    public MobileCancelFlightCommandValidator()
    {
        RuleFor(x => x.FlightId).NotEmpty();
        RuleFor(x => x.CanceledAtUtc).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.ClientMutationId).NotEmpty().MaximumLength(64);
    }
}

public sealed class MobileCancelFlightCommandHandler(
    IOperationsDbContext db,
    ISender sender,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<MobileCancelFlightCommand, MobileWriteResultDto>
{
    public async Task<Result<MobileWriteResultDto>> Handle(MobileCancelFlightCommand request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Forbidden("The request is not authenticated.", "Operations.WorkOrder.Unauthenticated");

        if (await MobileMutations.FindAsync(db, request.ClientMutationId, cancellationToken) is { } replay)
            return await MobileMutations.ReplayAsync(db, replay, cancellationToken);

        var payload = new WorkOrderEditableCommandPayload(
            ActualFlightNumber: null,
            AircraftTypeId: null,
            AircraftTailNumber: null,
            ActualArrivalUtc: null,
            ActualDepartureUtc: null,
            CanceledAtUtc: request.CanceledAtUtc,
            CancellationReason: request.Reason,
            Remarks: null,
            ServiceLines: [],
            Tasks: []);

        var workOrderId = Guid.NewGuid();
        db.MobileMutations.Add(MobileMutation.Record(
            request.ClientMutationId, userId, "cancel-flight",
            workOrderId, request.FlightId, clientFlightId: null, timeProvider.GetUtcNow()));

        var result = await sender.Send(
            new SubmitWorkOrderCommand(request.FlightId, WorkOrderType.Cancellation, payload, request.ClientMutationId, workOrderId),
            cancellationToken);

        if (result.IsFailure)
            return result.Error;

        return new MobileWriteResultDto(result.Value, request.FlightId, Idempotent: false);
    }
}

// --- Invite teammates (online-only, no outbox) ---------------------------------------------

public sealed record MobileInviteEmployeesCommand(
    Guid FlightId,
    IReadOnlyList<Guid> InviteeStaffMemberIds) : ICommand;

public sealed class MobileInviteEmployeesCommandValidator : AbstractValidator<MobileInviteEmployeesCommand>
{
    public MobileInviteEmployeesCommandValidator()
    {
        RuleFor(x => x.FlightId).NotEmpty();
        RuleFor(x => x.InviteeStaffMemberIds).NotEmpty();
    }
}

public sealed class MobileInviteEmployeesCommandHandler(
    IOperationsDbContext db,
    ISender sender) : ICommandHandler<MobileInviteEmployeesCommand>
{
    public async Task<Result> Handle(MobileInviteEmployeesCommand request, CancellationToken cancellationToken)
    {
        // The mobile client is online for invites but never holds a fresh RowVersion; resolve it
        // server-side. Add-only semantics and scope checks live in the inner command.
        var rowVersion = await db.Flights.AsNoTracking()
            .Where(f => f.Id == request.FlightId)
            .Select(f => f.RowVersion)
            .FirstOrDefaultAsync(cancellationToken);
        if (rowVersion is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        return await sender.Send(
            new InviteEmployeesToFlightCommand(request.FlightId, request.InviteeStaffMemberIds, rowVersion),
            cancellationToken);
    }
}
