using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Mobile;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Contracts.Auditing;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Features.Mobile;
using Operations.Domain.Authorization;
using Operations.Domain.Enumerations;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;

namespace Operations.Application.Features.WorkOrders;

public sealed record SubmitWorkOrderCommand(
    Guid FlightId,
    WorkOrderType Type,
    WorkOrderEditableCommandPayload Payload,
    string? ClientMutationId = null,
    Guid? WorkOrderId = null) : ICommand<Guid>;

public sealed class SubmitWorkOrderCommandValidator : AbstractValidator<SubmitWorkOrderCommand>
{
    public SubmitWorkOrderCommandValidator()
    {
        RuleFor(x => x.FlightId).NotEmpty();
        RuleFor(x => x.Payload).NotNull();
    }
}

public sealed class SubmitWorkOrderCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    WorkOrderInputBuilder inputBuilder,
    MasterDataResolver resolver,
    IFileStorage storage,
    IFlightTimelineWriter flightTimeline,
    IWorkOrderTimelineWriter workOrderTimeline,
    IMobileSyncBroadcaster mobileSync,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<SubmitWorkOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SubmitWorkOrderCommand request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } ownerUserId)
            return Error.Forbidden("The request is not authenticated.", "Operations.WorkOrder.Unauthenticated");

        var flight = await db.Flights
            .Include(f => f.PlannedServices)
            .Include(f => f.AssignedEmployees)
            .FirstOrDefaultAsync(f => f.Id == request.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var flightAccess = scopeResult.Value.EnsureFlightAccess(flight);
        if (flightAccess.IsFailure)
            return flightAccess.Error;

        var serviceAccess = await resolver.EnsurePerformedServicesAllowedAsync(
            request.Payload.ServiceLines?.Select(line => line.ServiceId).ToList() ?? [],
            scopeResult.Value.ManpowerTypeId,
            scopeResult.Value.IsAdministrator,
            cancellationToken);
        if (serviceAccess.IsFailure)
            return serviceAccess.Error;

        var alreadyActive = await db.WorkOrders.AsNoTracking().AnyAsync(w =>
            w.FlightId == flight.Id &&
            w.OwnerUserId == ownerUserId &&
            !w.IsMergeGenerated &&
            (w.Status == WorkOrderStatus.Submitted || w.Status == WorkOrderStatus.Returned || w.Status == WorkOrderStatus.Approved),
            cancellationToken);
        if (alreadyActive)
            return Error.Conflict("You already have an active work order for this flight.", "Operations.WorkOrder.ActiveExists");

        var input = await inputBuilder.BuildAsync(request.Payload, request.Type, flight.FlightNumber.Value, flight.Station.StationId, cancellationToken);
        if (input.IsFailure)
            return input.Error;

        Operations.Domain.ValueObjects.StaffMemberSnapshot? owner = null;
        if (scopeResult.Value.StaffMemberId is { } staffId)
        {
            var resolvedOwner = await resolver.StaffMemberAsync(staffId, cancellationToken);
            if (resolvedOwner.IsFailure)
                return resolvedOwner.Error;
            owner = resolvedOwner.Value;
        }

        var now = timeProvider.GetUtcNow();
        var workOrder = WorkOrder.SubmitNew(
            flight,
            request.Type,
            ownerUserId,
            owner,
            input.Value.ActualFlightNumber,
            input.Value.AircraftType,
            input.Value.AircraftTailNumber,
            input.Value.Actuals,
            input.Value.Cancellation,
            input.Value.Remarks,
            input.Value.ServiceLines,
            input.Value.Tasks,
            now,
            request.WorkOrderId);
        if (workOrder.IsFailure)
            return workOrder.Error;

        var inlineFiles = await WorkOrderInlineFileApplier.ApplyAsync(workOrder.Value, request.Payload, storage, now, cancellationToken);
        if (inlineFiles.IsFailure)
            return inlineFiles.Error;

        var flightState = flight.OnWorkOrderSubmitted(now);
        if (flightState.IsFailure)
        {
            await WorkOrderAttachmentStorage.DeleteAsync(storage, inlineFiles.Value, cancellationToken);
            return flightState.Error;
        }

        db.WorkOrders.Add(workOrder.Value);
        await workOrderTimeline.AppendAsync(workOrder.Value.Id, WorkOrderTimelineEventType.Submitted, now, cancellationToken: cancellationToken);
        await flightTimeline.AppendAsync(flight.Id, FlightTimelineEventType.WorkOrderSubmitted, now, details: workOrder.Value.Id.ToString(), cancellationToken: cancellationToken);

        MobileFlightSync.EnqueueUpsert(mobileSync, flight, request.ClientMutationId);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await WorkOrderAttachmentStorage.DeleteAsync(storage, inlineFiles.Value, cancellationToken);
            return Error.Conflict("A work order conflict occurred. Reload and try again.", "Operations.WorkOrder.Conflict");
        }

        return workOrder.Value.Id;
    }
}

public sealed record UpdateWorkOrderCommand(
    Guid Id,
    byte[] RowVersion,
    WorkOrderType Type,
    WorkOrderEditableCommandPayload Payload,
    string? ClientMutationId = null) : ICommand;

public sealed class UpdateWorkOrderCommandValidator : AbstractValidator<UpdateWorkOrderCommand>
{
    public UpdateWorkOrderCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
        RuleFor(x => x.Payload).NotNull();
    }
}

public sealed class UpdateWorkOrderCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    WorkOrderInputBuilder inputBuilder,
    MasterDataResolver resolver,
    IFileStorage storage,
    IWorkOrderTimelineWriter timeline,
    IMobileSyncBroadcaster mobileSync,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<UpdateWorkOrderCommand>
{
    public async Task<Result> Handle(UpdateWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await WorkOrderLoader.ForMutation(db.WorkOrders)
            .FirstOrDefaultAsync(w => w.Id == request.Id, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var access = scopeResult.Value.EnsureWorkOrderAccess(workOrder);
        if (access.IsFailure)
            return access.Error;
        var author = WorkOrderAuthorization.EnsureManageAccess(workOrder, user);
        if (author.IsFailure)
            return author.Error;

        var serviceAccess = await resolver.EnsurePerformedServicesAllowedAsync(
            request.Payload.ServiceLines?.Select(line => line.ServiceId).ToList() ?? [],
            scopeResult.Value.ManpowerTypeId,
            scopeResult.Value.IsAdministrator,
            cancellationToken);
        if (serviceAccess.IsFailure)
            return serviceAccess.Error;

        var previousType = workOrder.Type;
        var input = await inputBuilder.BuildAsync(request.Payload, request.Type, workOrder.ActualFlightNumber.Value, workOrder.Station.StationId, cancellationToken);
        if (input.IsFailure)
            return input.Error;

        var previousAttachmentReferences = WorkOrderAttachmentStorage.References(workOrder);

        var now = timeProvider.GetUtcNow();
        db.SetOriginalRowVersion(workOrder, request.RowVersion);
        var update = workOrder.UpdateDetails(
            request.Type,
            input.Value.ActualFlightNumber,
            input.Value.AircraftType,
            input.Value.AircraftTailNumber,
            input.Value.Actuals,
            input.Value.Cancellation,
            input.Value.Remarks,
            input.Value.ServiceLines,
            input.Value.Tasks,
            now);
        if (update.IsFailure)
            return update.Error;

        var inlineFiles = await WorkOrderInlineFileApplier.ApplyAsync(workOrder, request.Payload, storage, now, cancellationToken);
        if (inlineFiles.IsFailure)
            return inlineFiles.Error;

        var timelineType = previousType == request.Type
            ? WorkOrderTimelineEventType.Updated
            : request.Type == WorkOrderType.Completion
                ? WorkOrderTimelineEventType.ConvertedToCompletion
                : WorkOrderTimelineEventType.ConvertedToCancellation;
        await timeline.AppendAsync(workOrder.Id, timelineType, now, cancellationToken: cancellationToken);

        // The mobile cache stores the caller's work order embedded on the flight row, so an update
        // is a flight upsert to every audience that may hold the row.
        var syncFlight = await db.Flights.AsNoTracking()
            .Include(f => f.PlannedServices)
            .Include(f => f.AssignedEmployees)
            .FirstOrDefaultAsync(f => f.Id == workOrder.FlightId, cancellationToken);
        if (syncFlight is not null)
            MobileFlightSync.EnqueueUpsert(mobileSync, syncFlight, request.ClientMutationId);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await WorkOrderAttachmentStorage.DeleteAsync(storage, inlineFiles.Value, cancellationToken);
            return ConcurrencyErrors.Stale;
        }
        catch (DbUpdateException)
        {
            await WorkOrderAttachmentStorage.DeleteAsync(storage, inlineFiles.Value, cancellationToken);
            return Error.Conflict("Work order update conflicted with another update. Reload and try again.", "Operations.WorkOrder.UpdateConflict");
        }

        var currentAttachmentReferences = WorkOrderAttachmentStorage.References(workOrder);
        await WorkOrderAttachmentStorage.DeleteAsync(
            storage,
            previousAttachmentReferences.Except(currentAttachmentReferences, StringComparer.OrdinalIgnoreCase),
            cancellationToken);

        return Result.Success();
    }
}

public sealed record DeleteWorkOrderCommand(Guid Id, byte[] RowVersion, string? ClientMutationId = null) : ICommand;

public sealed class DeleteWorkOrderCommandValidator : AbstractValidator<DeleteWorkOrderCommand>
{
    public DeleteWorkOrderCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class DeleteWorkOrderCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IFileStorage storage,
    IFlightTimelineWriter flightTimeline,
    IMobileSyncBroadcaster mobileSync,
    IUserContext user,
    IAuditContext auditContext,
    TimeProvider timeProvider) : ICommandHandler<DeleteWorkOrderCommand>
{
    public async Task<Result> Handle(DeleteWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await WorkOrderLoader.ForMutation(db.WorkOrders)
            .FirstOrDefaultAsync(w => w.Id == request.Id, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var access = scopeResult.Value.EnsureWorkOrderAccess(workOrder);
        if (access.IsFailure)
            return access.Error;
        var author = WorkOrderAuthorization.EnsureDeleteAccess(workOrder, user);
        if (author.IsFailure)
            return author.Error;

        if (workOrder.Status == WorkOrderStatus.Approved)
            return Error.Conflict("Approved work orders cannot be deleted.", "Operations.WorkOrder.ApprovedDeleteBlocked");
        if (workOrder.Status == WorkOrderStatus.Merged)
            return Error.Conflict("Merged work orders cannot be deleted.", "Operations.WorkOrder.MergedDeleteBlocked");

        var attachmentReferences = WorkOrderAttachmentStorage.References(workOrder);

        db.SetOriginalRowVersion(workOrder, request.RowVersion);
        db.WorkOrders.Remove(workOrder);

        var now = timeProvider.GetUtcNow();
        await flightTimeline.AppendAsync(workOrder.FlightId, FlightTimelineEventType.WorkOrderSubmitted, now,
            details: $"Work order {workOrder.Id} deleted.", cancellationToken: cancellationToken);
        db.EnqueueAudit(auditContext, "operations", "WorkOrder", workOrder.Id, "WorkOrder", workOrder.Id, AuditActions.Deleted,
            metadata: $"FlightId={workOrder.FlightId}");

        var syncFlight = await db.Flights.AsNoTracking()
            .Include(f => f.PlannedServices)
            .Include(f => f.AssignedEmployees)
            .FirstOrDefaultAsync(f => f.Id == workOrder.FlightId, cancellationToken);
        if (syncFlight is not null)
            MobileFlightSync.EnqueueUpsert(mobileSync, syncFlight, request.ClientMutationId);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }

        await WorkOrderAttachmentStorage.DeleteAsync(storage, attachmentReferences, cancellationToken);
        return Result.Success();
    }
}

public sealed record ApproveWorkOrderCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed class ApproveWorkOrderCommandValidator : AbstractValidator<ApproveWorkOrderCommand>
{
    public ApproveWorkOrderCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class ApproveWorkOrderCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IWorkOrderNumberAllocator allocator,
    IWorkOrderTimelineWriter workOrderTimeline,
    IFlightTimelineWriter flightTimeline,
    IMobileSyncBroadcaster mobileSync,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<ApproveWorkOrderCommand>
{
    public async Task<Result> Handle(ApproveWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await WorkOrderLoader.ForMutation(db.WorkOrders)
            .FirstOrDefaultAsync(w => w.Id == request.Id, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var flight = await db.Flights
            .Include(f => f.PlannedServices)
            .Include(f => f.AssignedEmployees)
            .FirstOrDefaultAsync(f => f.Id == workOrder.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var station = scopeResult.Value.EnsureStation(workOrder.Station.StationId);
        if (station.IsFailure)
            return station.Error;

        var alreadyApproved = await db.WorkOrders.AsNoTracking().AnyAsync(w =>
            w.FlightId == workOrder.FlightId && w.Id != workOrder.Id && w.Status == WorkOrderStatus.Approved,
            cancellationToken);
        if (alreadyApproved)
            return Error.Conflict("This flight already has an approved work order.", "Operations.WorkOrder.FlightAlreadyApproved");

        var allocation = await allocator.AllocateAsync(workOrder.Station, cancellationToken);
        if (allocation.IsFailure)
            return allocation.Error;

        db.SetOriginalRowVersion(workOrder, request.RowVersion);
        var now = timeProvider.GetUtcNow();
        var approve = workOrder.Approve(allocation.Value.Sequence, allocation.Value.Number, user.UserId ?? Guid.Empty, now);
        if (approve.IsFailure)
            return approve.Error;

        var settle = workOrder.Type == WorkOrderType.Completion
            ? flight.SettleCompleted(now)
            : flight.SettleCanceled(now);
        if (settle.IsFailure)
            return settle.Error;

        await workOrderTimeline.AppendAsync(workOrder.Id, WorkOrderTimelineEventType.Approved, now, details: workOrder.ApprovalNumber, cancellationToken: cancellationToken);
        await workOrderTimeline.AppendAsync(workOrder.Id, WorkOrderTimelineEventType.NumberAssigned, now, details: workOrder.ApprovalNumber, cancellationToken: cancellationToken);
        await flightTimeline.AppendAsync(flight.Id,
            workOrder.Type == WorkOrderType.Completion ? FlightTimelineEventType.FlightCompleted : FlightTimelineEventType.FlightCanceled,
            now,
            details: workOrder.ApprovalNumber,
            cancellationToken: cancellationToken);

        MobileFlightSync.EnqueueUpsert(mobileSync, flight);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }
        catch (DbUpdateException)
        {
            return Error.Conflict("Approval conflicted with another work order update. Reload and try again.", "Operations.WorkOrder.ApprovalConflict");
        }

        return Result.Success();
    }
}

public sealed record ReturnWorkOrderCommand(Guid Id, byte[] RowVersion, string Reason) : ICommand;

public sealed class ReturnWorkOrderCommandValidator : AbstractValidator<ReturnWorkOrderCommand>
{
    public ReturnWorkOrderCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class ReturnWorkOrderCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IWorkOrderTimelineWriter workOrderTimeline,
    IFlightTimelineWriter flightTimeline,
    IMobileSyncBroadcaster mobileSync,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<ReturnWorkOrderCommand>
{
    public async Task<Result> Handle(ReturnWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await WorkOrderLoader.ForMutation(db.WorkOrders)
            .FirstOrDefaultAsync(w => w.Id == request.Id, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var flight = await db.Flights
            .Include(f => f.PlannedServices)
            .Include(f => f.AssignedEmployees)
            .FirstOrDefaultAsync(f => f.Id == workOrder.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var station = scopeResult.Value.EnsureStation(workOrder.Station.StationId);
        if (station.IsFailure)
            return station.Error;

        db.SetOriginalRowVersion(workOrder, request.RowVersion);
        var now = timeProvider.GetUtcNow();
        var releasedNumber = workOrder.ApprovalNumber;
        var returned = workOrder.Return(user.UserId ?? Guid.Empty, request.Reason, now);
        if (returned.IsFailure)
            return returned.Error;

        var reopen = flight.ReopenToInProgress(now);
        if (reopen.IsFailure)
            return reopen.Error;

        await workOrderTimeline.AppendAsync(workOrder.Id, WorkOrderTimelineEventType.Returned, now, details: request.Reason, cancellationToken: cancellationToken);
        if (!string.IsNullOrWhiteSpace(releasedNumber))
            await workOrderTimeline.AppendAsync(workOrder.Id, WorkOrderTimelineEventType.NumberReleased, now, details: releasedNumber, cancellationToken: cancellationToken);
        await flightTimeline.AppendAsync(flight.Id, FlightTimelineEventType.FlightReopened, now, details: releasedNumber, cancellationToken: cancellationToken);

        MobileFlightSync.EnqueueUpsert(mobileSync, flight);

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

public sealed record MergeWorkOrdersCommand(
    Guid FlightId,
    IReadOnlyList<Guid> SourceWorkOrderIds,
    WorkOrderType Type,
    WorkOrderEditableCommandPayload Payload,
    bool ApproveImmediately) : ICommand<Guid>;

public sealed class MergeWorkOrdersCommandValidator : AbstractValidator<MergeWorkOrdersCommand>
{
    public MergeWorkOrdersCommandValidator()
    {
        RuleFor(x => x.FlightId).NotEmpty();
        RuleFor(x => x.SourceWorkOrderIds)
            .NotNull()
            .Must(ids => ids is { Count: >= 2 }).WithMessage("At least two source work orders are required.")
            .Must(ids => ids is not null && ids.Distinct().Count() == ids.Count).WithMessage("Source work orders must be unique.");
        RuleFor(x => x.Payload).NotNull();
    }
}

public sealed class MergeWorkOrdersCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    WorkOrderInputBuilder inputBuilder,
    MasterDataResolver resolver,
    IWorkOrderNumberAllocator allocator,
    IWorkOrderTimelineWriter workOrderTimeline,
    IFlightTimelineWriter flightTimeline,
    IMobileSyncBroadcaster mobileSync,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<MergeWorkOrdersCommand, Guid>
{
    public async Task<Result<Guid>> Handle(MergeWorkOrdersCommand request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } ownerUserId)
            return Error.Forbidden("The request is not authenticated.", "Operations.WorkOrder.Unauthenticated");

        if (request.ApproveImmediately && !user.HasPermission(OperationsPermissions.WorkOrders.Approve))
        {
            return Error.Forbidden(
                "Approving a merged work order requires work-order approval permission.",
                "Operations.WorkOrder.ApproveForbidden");
        }

        if (request.SourceWorkOrderIds is not { Count: >= 2 })
            return Error.Validation("At least two source work orders are required.", "Operations.WorkOrder.MergeSourceCount");
        if (request.SourceWorkOrderIds.Distinct().Count() != request.SourceWorkOrderIds.Count)
            return Error.Validation("Source work orders must be unique.", "Operations.WorkOrder.MergeSourceDuplicate");

        var flight = await db.Flights
            .Include(f => f.PlannedServices)
            .Include(f => f.AssignedEmployees)
            .FirstOrDefaultAsync(f => f.Id == request.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var station = scopeResult.Value.EnsureStation(flight.Station.StationId);
        if (station.IsFailure)
            return station.Error;

        var serviceAccess = await resolver.EnsurePerformedServicesAllowedAsync(
            request.Payload.ServiceLines?.Select(line => line.ServiceId).ToList() ?? [],
            scopeResult.Value.ManpowerTypeId,
            scopeResult.Value.IsAdministrator,
            cancellationToken);
        if (serviceAccess.IsFailure)
            return serviceAccess.Error;

        var sourceIds = request.SourceWorkOrderIds.ToList();
        var sources = await WorkOrderLoader.ForMutation(db.WorkOrders)
            .Where(w => sourceIds.Contains(w.Id))
            .ToListAsync(cancellationToken);
        if (sources.Count != sourceIds.Count)
            return Error.NotFound("One or more source work orders were not found.", "Operations.WorkOrder.MergeSourceNotFound");

        foreach (var source in sources)
        {
            var sourceStation = scopeResult.Value.EnsureStation(source.Station.StationId);
            if (sourceStation.IsFailure)
                return sourceStation.Error;
            if (source.FlightId != request.FlightId)
                return Error.Validation("All source work orders must belong to the selected flight.", "Operations.WorkOrder.MergeFlightMismatch");
            if (source.IsMergeGenerated)
                return Error.Conflict("Merge-generated work orders cannot be used as merge sources.", "Operations.WorkOrder.MergeGeneratedSource");
            if (!source.IsEditable)
                return Error.Conflict("Only submitted or returned work orders can be merged.", "Operations.WorkOrder.MergeSourceLocked");
        }
        if (sources.Any(source => source.Type != request.Type) || sources.Select(source => source.Type).Distinct().Count() != 1)
            return Error.Validation("All source work orders must have the same type.", "Operations.WorkOrder.MergeTypeMismatch");

        var alreadyApproved = await db.WorkOrders.AsNoTracking().AnyAsync(w =>
            w.FlightId == request.FlightId && w.Status == WorkOrderStatus.Approved,
            cancellationToken);
        if (alreadyApproved)
            return Error.Conflict("This flight already has an approved work order.", "Operations.WorkOrder.FlightAlreadyApproved");

        var input = await inputBuilder.BuildAsync(request.Payload, request.Type, flight.FlightNumber.Value, flight.Station.StationId, cancellationToken);
        if (input.IsFailure)
            return input.Error;

        StaffMemberSnapshot? owner = null;
        if (scopeResult.Value.StaffMemberId is { } staffId)
        {
            var resolvedOwner = await resolver.StaffMemberAsync(staffId, cancellationToken);
            if (resolvedOwner.IsFailure)
                return resolvedOwner.Error;
            owner = resolvedOwner.Value;
        }

        var now = timeProvider.GetUtcNow();
        var generated = WorkOrder.SubmitMerged(
            flight,
            request.Type,
            ownerUserId,
            owner,
            input.Value.ActualFlightNumber,
            input.Value.AircraftType,
            input.Value.AircraftTailNumber,
            input.Value.Actuals,
            input.Value.Cancellation,
            input.Value.Remarks,
            input.Value.ServiceLines,
            input.Value.Tasks.Select(task => task with { Id = null }).ToList(),
            now);
        if (generated.IsFailure)
            return generated.Error;

        db.WorkOrders.Add(generated.Value);

        var flightState = flight.OnWorkOrderSubmitted(now);
        if (flightState.IsFailure)
            return flightState.Error;

        var sourceDetails = string.Join(", ", sources.Select(s => s.Id));
        await workOrderTimeline.AppendAsync(generated.Value.Id, WorkOrderTimelineEventType.Merged, now,
            details: $"Generated from {sources.Count} source work orders: {sourceDetails}.", cancellationToken: cancellationToken);

        foreach (var source in sources)
        {
            var merged = source.MarkMergedInto(generated.Value.Id, now);
            if (merged.IsFailure)
                return merged.Error;

            await workOrderTimeline.AppendAsync(source.Id, WorkOrderTimelineEventType.Merged, now,
                details: generated.Value.Id.ToString(), cancellationToken: cancellationToken);
        }

        await flightTimeline.AppendAsync(flight.Id, FlightTimelineEventType.WorkOrderSubmitted, now,
            details: $"Merged work order {generated.Value.Id} generated from {sources.Count} source work orders.",
            cancellationToken: cancellationToken);

        if (request.ApproveImmediately)
        {
            var allocation = await allocator.AllocateAsync(generated.Value.Station, cancellationToken);
            if (allocation.IsFailure)
                return allocation.Error;

            var approve = generated.Value.Approve(allocation.Value.Sequence, allocation.Value.Number, ownerUserId, now);
            if (approve.IsFailure)
                return approve.Error;

            var settle = generated.Value.Type == WorkOrderType.Completion
                ? flight.SettleCompleted(now)
                : flight.SettleCanceled(now);
            if (settle.IsFailure)
                return settle.Error;

            await workOrderTimeline.AppendAsync(generated.Value.Id, WorkOrderTimelineEventType.Approved, now, details: generated.Value.ApprovalNumber, cancellationToken: cancellationToken);
            await workOrderTimeline.AppendAsync(generated.Value.Id, WorkOrderTimelineEventType.NumberAssigned, now, details: generated.Value.ApprovalNumber, cancellationToken: cancellationToken);
            await flightTimeline.AppendAsync(flight.Id,
                generated.Value.Type == WorkOrderType.Completion ? FlightTimelineEventType.FlightCompleted : FlightTimelineEventType.FlightCanceled,
                now,
                details: generated.Value.ApprovalNumber,
                cancellationToken: cancellationToken);
        }

        MobileFlightSync.EnqueueUpsert(mobileSync, flight);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Error.Conflict("Work order merge conflicted with another update. Reload and try again.", "Operations.WorkOrder.MergeConflict");
        }

        return generated.Value.Id;
    }
}

internal static class WorkOrderLoader
{
    public static IQueryable<WorkOrder> ForMutation(IQueryable<WorkOrder> query) =>
        query
            .Include(w => w.ServiceLines).ThenInclude(line => line.PerformedBy)
            .Include(w => w.ServiceLines).ThenInclude(line => line.Attachments)
            .Include(w => w.Tasks).ThenInclude(t => t.Employees)
            .Include(w => w.Tasks).ThenInclude(t => t.Tools)
            .Include(w => w.Tasks).ThenInclude(t => t.Materials)
            .Include(w => w.Tasks).ThenInclude(t => t.GeneralSupports)
            .Include(w => w.Tasks).ThenInclude(t => t.Attachments);
}

internal static class WorkOrderAuthorization
{
    public static Result EnsureManageAccess(WorkOrder workOrder, IUserContext user) =>
        EnsureOwnerOrPermission(workOrder, user, OperationsPermissions.WorkOrders.ManageOthers);

    public static Result EnsureDeleteAccess(WorkOrder workOrder, IUserContext user) =>
        EnsureOwnerOrPermission(workOrder, user, OperationsPermissions.WorkOrders.DeleteOthers);

    private static Result EnsureOwnerOrPermission(WorkOrder workOrder, IUserContext user, string permission) =>
        (user.UserId is { } userId && workOrder.OwnerUserId == userId) || user.HasPermission(permission)
            ? Result.Success()
            : Error.Forbidden("You can only modify your own work orders.", "Operations.WorkOrder.NotOwner");
}
