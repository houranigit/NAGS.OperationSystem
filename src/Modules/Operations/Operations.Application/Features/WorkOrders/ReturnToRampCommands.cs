using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Mobile;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Features.Mobile;
using Operations.Domain.Enumerations;

namespace Operations.Application.Features.WorkOrders;

public sealed record RecordReturnToRampForFlightCommand(
    Guid FlightId,
    WorkOrderReturnToRampCommand ReturnToRamp,
    string? ClientMutationId = null) : ICommand<Guid>;

public sealed class RecordReturnToRampForFlightCommandValidator : AbstractValidator<RecordReturnToRampForFlightCommand>
{
    public RecordReturnToRampForFlightCommandValidator()
    {
        RuleFor(command => command.FlightId).NotEmpty();
        RuleFor(command => command.ReturnToRamp).NotNull();
    }
}

public sealed class RecordReturnToRampForFlightCommandHandler(
    IOperationsDbContext db,
    ISender sender,
    IUserContext user) : ICommandHandler<RecordReturnToRampForFlightCommand, Guid>
{
    public async Task<Result<Guid>> Handle(
        RecordReturnToRampForFlightCommand request,
        CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Forbidden("The request is not authenticated.", "Operations.WorkOrder.Unauthenticated");

        var resolved = await ReturnToRampWorkOrderResolver.ResolveAsync(
            db, request.FlightId, userId, cancellationToken);
        if (resolved.IsFailure)
            return resolved.Error;

        return await sender.Send(
            new RecordReturnToRampOnWorkOrderCommand(
                resolved.Value,
                request.ReturnToRamp,
                request.ClientMutationId),
            cancellationToken);
    }
}

/// <summary>
/// Resolves the canonical occurrence owner before a write starts. Mobile mutation rows use this
/// same result so their work-order id is committed atomically with the occurrence.
/// </summary>
internal static class ReturnToRampWorkOrderResolver
{
    internal static async Task<Result<Guid>> ResolveAsync(
        IOperationsDbContext db,
        Guid flightId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var flight = await db.Flights.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == flightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");
        if (flight.Status is not (FlightStatus.InProgress or FlightStatus.Completed))
            return Error.Conflict("Return to ramp can only be recorded for an in-progress or completed flight.", "Operations.ReturnToRamp.FlightStatusInvalid");

        var candidates = db.WorkOrders.AsNoTracking()
            .Where(item => item.FlightId == flight.Id && item.Type == WorkOrderType.Completion);
        Guid? workOrderId = flight.Status == FlightStatus.Completed
            ? await candidates
                .Where(item => item.Status == WorkOrderStatus.Approved)
                .Select(item => (Guid?)item.Id)
                .FirstOrDefaultAsync(cancellationToken)
            : await candidates
                .Where(item => item.OwnerUserId == userId &&
                    (item.Status == WorkOrderStatus.Submitted || item.Status == WorkOrderStatus.Returned))
                .OrderByDescending(item => item.UpdatedAtUtc ?? item.CreatedAtUtc)
                .Select(item => (Guid?)item.Id)
                .FirstOrDefaultAsync(cancellationToken);

        if (workOrderId is null)
            return Error.Conflict(
                flight.Status == FlightStatus.Completed
                    ? "The completed flight has no approved completion work order."
                    : "Create or reopen your completion work order before recording return to ramp.",
                "Operations.ReturnToRamp.WorkOrderUnavailable");

        return workOrderId.Value;
    }
}

public sealed record RecordReturnToRampOnWorkOrderCommand(
    Guid WorkOrderId,
    WorkOrderReturnToRampCommand ReturnToRamp,
    string? ClientMutationId = null) : ICommand<Guid>;

public sealed class RecordReturnToRampOnWorkOrderCommandValidator : AbstractValidator<RecordReturnToRampOnWorkOrderCommand>
{
    public RecordReturnToRampOnWorkOrderCommandValidator()
    {
        RuleFor(command => command.WorkOrderId).NotEmpty();
        RuleFor(command => command.ReturnToRamp).NotNull();
    }
}

public sealed class RecordReturnToRampOnWorkOrderCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    WorkOrderInputBuilder inputBuilder,
    MasterDataResolver resolver,
    IFileStorage storage,
    IWorkOrderTimelineWriter workOrderTimeline,
    IFlightTimelineWriter flightTimeline,
    IMobileSyncBroadcaster mobileSync,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<RecordReturnToRampOnWorkOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(
        RecordReturnToRampOnWorkOrderCommand request,
        CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Forbidden("The request is not authenticated.", "Operations.WorkOrder.Unauthenticated");

        var workOrder = await WorkOrderLoader.ForMutation(db.WorkOrders)
            .FirstOrDefaultAsync(item => item.Id == request.WorkOrderId, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var flight = await db.Flights
            .Include(item => item.PlannedServices)
            .Include(item => item.AssignedEmployees)
            .FirstOrDefaultAsync(item => item.Id == workOrder.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");
        if (flight.Status is not (FlightStatus.InProgress or FlightStatus.Completed))
            return Error.Conflict("Return to ramp can only be recorded for an in-progress or completed flight.", "Operations.ReturnToRamp.FlightStatusInvalid");

        var scopeResult = await scope.ResolveForWriteAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var appendToApproved = flight.Status == FlightStatus.Completed &&
            workOrder.Status == WorkOrderStatus.Approved &&
            workOrder.Type == WorkOrderType.Completion;
        var access = appendToApproved
            ? scopeResult.Value.EnsureFlightAccess(flight)
            : scopeResult.Value.EnsureWorkOrderAccess(workOrder);
        if (access.IsFailure)
            return access.Error;
        if (!appendToApproved)
        {
            var author = WorkOrderAuthorization.EnsureManageAccess(workOrder, user);
            if (author.IsFailure)
                return author.Error;
            if (flight.Status != FlightStatus.InProgress || !workOrder.IsEditable)
                return Error.Conflict("The in-progress flight requires an editable completion work order.", "Operations.ReturnToRamp.WorkOrderUnavailable");
        }

        var serviceAccess = await resolver.EnsurePerformedServicesAllowedAsync(
            (request.ReturnToRamp.ServiceLines ?? []).Select(item => item.ServiceId).Distinct().ToList(),
            scopeResult.Value.ManpowerTypeId,
            scopeResult.Value.IsAdministrator,
            cancellationToken);
        if (serviceAccess.IsFailure)
            return serviceAccess.Error;

        var input = await inputBuilder.BuildReturnToRampAsync(
            request.ReturnToRamp,
            workOrder.Station.StationId,
            cancellationToken);
        if (input.IsFailure)
            return input.Error;

        var now = timeProvider.GetUtcNow();
        var append = workOrder.AppendReturnToRamp(input.Value, userId, now, appendToApproved);
        if (append.IsFailure)
            return append.Error;

        var attachmentPayload = new WorkOrderEditableCommandPayload(
            ActualFlightNumber: null,
            AircraftTypeId: null,
            AircraftTailNumber: null,
            ActualArrivalUtc: null,
            ActualDepartureUtc: null,
            CanceledAtUtc: null,
            CancellationReason: null,
            Remarks: null,
            ServiceLines: [],
            Tasks: [],
            CustomerSignature: null,
            ReturnToRamps: [request.ReturnToRamp with { Id = append.Value.Id }]);
        var inlineFiles = await WorkOrderInlineFileApplier.ApplyAsync(
            workOrder,
            attachmentPayload,
            storage,
            now,
            cancellationToken);
        if (inlineFiles.IsFailure)
            return inlineFiles.Error;

        var details = $"{append.Value.Id}; {append.Value.Window.From:O} - {append.Value.Window.To:O}";
        await workOrderTimeline.AppendAsync(
            workOrder.Id,
            WorkOrderTimelineEventType.ReturnToRampRecorded,
            now,
            details: details,
            cancellationToken: cancellationToken);
        await flightTimeline.AppendAsync(
            flight.Id,
            FlightTimelineEventType.ReturnToRampRecorded,
            now,
            details: details,
            cancellationToken: cancellationToken);

        MobileFlightSync.EnqueueUpsert(mobileSync, flight, request.ClientMutationId);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await WorkOrderAttachmentStorage.DeleteAsync(storage, inlineFiles.Value, cancellationToken);
            return Error.Conflict("Return to ramp conflicted with another update. Reload and try again.", "Operations.ReturnToRamp.Conflict");
        }

        return append.Value.Id;
    }
}
