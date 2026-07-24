using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Mobile;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Features.Flights;
using Operations.Application.Features.Mobile;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;

namespace Operations.Application.Features.WorkOrders;

public sealed record CreateAdHocWorkOrderCommand(
    Guid CustomerId,
    Guid StationId,
    string FlightNumber,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    Guid? AircraftTypeId,
    IReadOnlyList<Guid> PlannedServiceIds,
    IReadOnlyList<Guid> AssignedStaffMemberIds,
    WorkOrderType Type,
    WorkOrderEditableCommandPayload Payload,
    string? ClientMutationId = null,
    Guid? FlightId = null,
    Guid? WorkOrderId = null) : ICommand<Guid>;

public sealed class CreateAdHocWorkOrderCommandValidator : AbstractValidator<CreateAdHocWorkOrderCommand>
{
    public CreateAdHocWorkOrderCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.StationId).NotEmpty();
        RuleFor(x => x.FlightNumber).NotEmpty().MaximumLength(12);
        RuleFor(x => x.PlannedServiceIds).NotNull();
        RuleFor(x => x.AssignedStaffMemberIds).NotNull();
        RuleFor(x => x.Payload).NotNull();
    }
}

public sealed class CreateAdHocWorkOrderCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    MasterDataResolver resolver,
    WorkOrderInputBuilder inputBuilder,
    IFileStorage storage,
    IFlightTimelineWriter flightTimeline,
    IWorkOrderTimelineWriter workOrderTimeline,
    IMobileSyncBroadcaster mobileSync,
    IUserContext user,
    IAuditContext auditContext,
    TimeProvider timeProvider) : ICommandHandler<CreateAdHocWorkOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateAdHocWorkOrderCommand request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } ownerUserId)
            return Error.Forbidden("The request is not authenticated.", "Operations.WorkOrder.Unauthenticated");

        var scopeResult = await scope.ResolveForWriteAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var stationCheck = scopeResult.Value.EnsureStation(request.StationId);
        if (stationCheck.IsFailure)
            return stationCheck.Error;

        var serviceAccess = await resolver.EnsurePerformedServicesAllowedAsync(
            request.Payload.ServiceLines?.Select(line => line.ServiceId).ToList() ?? [],
            scopeResult.Value.ManpowerTypeId,
            scopeResult.Value.IsAdministrator,
            cancellationToken);
        if (serviceAccess.IsFailure)
            return serviceAccess.Error;

        const bool allowEmptyPlannedServices = true;

        var flightInput = await FlightBuildHelpers.BuildAsync(
            resolver,
            request.CustomerId,
            request.StationId,
            WellKnownMasterDataIds.AdHocOperationType,
            request.AircraftTypeId,
            request.FlightNumber,
            request.ScheduledArrivalUtc,
            request.ScheduledDepartureUtc,
            request.PlannedServiceIds,
            cancellationToken);
        if (flightInput.IsFailure)
            return flightInput.Error;

        if (PerLandingAssignmentGuard.HasPerLandingAssignedStaff(flightInput.Value.PlannedServices, request.AssignedStaffMemberIds))
            return PerLandingAssignmentGuard.Error();

        var assignedStaffIds = request.AssignedStaffMemberIds.Distinct().ToList();
        if (!flightInput.Value.PlannedServices.Any(s => PerLandingPolicy.IsAircraftPerLanding(s.ServiceId)) &&
            scopeResult.Value.StaffMemberId is { } staffId &&
            !assignedStaffIds.Contains(staffId))
        {
            assignedStaffIds.Add(staffId);
        }

        var employees = await resolver.StaffMembersForStationAsync(assignedStaffIds, request.StationId, cancellationToken);
        if (employees.IsFailure)
            return employees.Error;

        var workOrderInput = await inputBuilder.BuildAsync(request.Payload, request.Type, flightInput.Value.FlightNumber.Value, request.StationId, cancellationToken);
        if (workOrderInput.IsFailure)
            return workOrderInput.Error;

        StaffMemberSnapshot? owner = null;
        if (scopeResult.Value.StaffMemberId is { } ownerStaffId)
        {
            var resolvedOwner = await resolver.StaffMemberAsync(ownerStaffId, cancellationToken);
            if (resolvedOwner.IsFailure)
                return resolvedOwner.Error;
            owner = resolvedOwner.Value;
        }

        var now = timeProvider.GetUtcNow();
        var b = flightInput.Value;
        var flight = Flight.ScheduleNew(
            b.Customer,
            b.Station,
            b.OperationType,
            b.FlightNumber,
            b.Schedule,
            b.AircraftType,
            b.PlannedServices,
            employees.Value,
            contractId: null,
            contractNumber: null,
            createdByUserId: ownerUserId,
            now: now,
            id: request.FlightId,
            allowEmptyPlannedServices: allowEmptyPlannedServices);
        if (flight.IsFailure)
            return flight.Error;

        var workOrder = WorkOrder.SubmitNew(
            flight.Value,
            request.Type,
            ownerUserId,
            owner,
            workOrderInput.Value.ActualFlightNumber,
            workOrderInput.Value.AircraftType,
            workOrderInput.Value.AircraftTailNumber,
            workOrderInput.Value.Actuals,
            workOrderInput.Value.Cancellation,
            workOrderInput.Value.Remarks,
            workOrderInput.Value.ServiceLines,
            workOrderInput.Value.Tasks,
            now,
            request.WorkOrderId);
        if (workOrder.IsFailure)
            return workOrder.Error;

        var inlineFiles = await WorkOrderInlineFileApplier.ApplyAsync(workOrder.Value, request.Payload, storage, now, cancellationToken);
        if (inlineFiles.IsFailure)
            return inlineFiles.Error;

        var flightState = flight.Value.OnWorkOrderSubmitted(now);
        if (flightState.IsFailure)
        {
            await WorkOrderAttachmentStorage.DeleteAsync(storage, inlineFiles.Value, cancellationToken);
            return flightState.Error;
        }

        db.Flights.Add(flight.Value);
        db.WorkOrders.Add(workOrder.Value);

        await flightTimeline.AppendAsync(flight.Value.Id, FlightTimelineEventType.FlightScheduled, now,
            details: "Ad-hoc flight created from work order.", cancellationToken: cancellationToken);
        foreach (var employee in employees.Value)
            await flightTimeline.AppendAsync(flight.Value.Id, FlightTimelineEventType.EmployeeAssigned, now,
                details: employee.FullName, cancellationToken: cancellationToken);
        await flightTimeline.AppendAsync(flight.Value.Id, FlightTimelineEventType.WorkOrderSubmitted, now,
            details: workOrder.Value.Id.ToString(), cancellationToken: cancellationToken);
        await workOrderTimeline.AppendAsync(workOrder.Value.Id, WorkOrderTimelineEventType.Submitted, now, cancellationToken: cancellationToken);

        FlightAssignmentEvents.Enqueue(
            db,
            flight.Value,
            employees.Value.Select(employee => employee.StaffMemberId),
            user,
            auditContext,
            now);
        MobileFlightSync.EnqueueUpsert(mobileSync, flight.Value, request.ClientMutationId);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await WorkOrderAttachmentStorage.DeleteAsync(storage, inlineFiles.Value, cancellationToken);
            return Error.Conflict("An ad-hoc work order conflict occurred. Reload and try again.", "Operations.WorkOrder.AdHocConflict");
        }

        return workOrder.Value.Id;
    }
}
