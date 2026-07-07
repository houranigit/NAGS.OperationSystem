using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;

namespace Operations.Application.Features.Flights;

// --- Schedule flight --------------------------------------------------------

public sealed record ScheduleFlightCommand(
    Guid CustomerId,
    Guid StationId,
    Guid OperationTypeId,
    string FlightNumber,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    Guid? AircraftTypeId,
    IReadOnlyList<Guid> PlannedServiceIds,
    IReadOnlyList<Guid> AssignedStaffMemberIds) : ICommand<Guid>;

public sealed class ScheduleFlightCommandValidator : AbstractValidator<ScheduleFlightCommand>
{
    public ScheduleFlightCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.StationId).NotEmpty();
        RuleFor(x => x.OperationTypeId).NotEmpty();
        RuleFor(x => x.FlightNumber).NotEmpty().MaximumLength(12);
        RuleFor(x => x.PlannedServiceIds).NotEmpty();
    }
}

public sealed class ScheduleFlightCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    MasterDataResolver resolver,
    IFlightTimelineWriter timeline,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<ScheduleFlightCommand, Guid>
{
    public async Task<Result<Guid>> Handle(ScheduleFlightCommand request, CancellationToken cancellationToken)
    {
        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var stationCheck = scopeResult.Value.EnsureStation(request.StationId);
        if (stationCheck.IsFailure)
            return stationCheck.Error;

        if (request.OperationTypeId == WellKnownMasterDataIds.AdHocOperationType)
            return Error.Validation("Scheduled flights cannot use the Ad Hoc operation type.", "Operations.Flight.AdHocNotSchedulable");

        var build = await FlightBuildHelpers.BuildAsync(resolver, request.CustomerId, request.StationId, request.OperationTypeId,
            request.AircraftTypeId, request.FlightNumber, request.ScheduledArrivalUtc, request.ScheduledDepartureUtc,
            request.PlannedServiceIds, cancellationToken);
        if (build.IsFailure)
            return build.Error;

        if (PerLandingAssignmentGuard.HasPerLandingAssignedStaff(build.Value.PlannedServices, request.AssignedStaffMemberIds))
            return PerLandingAssignmentGuard.Error();

        var employees = await resolver.StaffMembersForStationAsync(request.AssignedStaffMemberIds, request.StationId, cancellationToken);
        if (employees.IsFailure)
            return employees.Error;

        var now = timeProvider.GetUtcNow();
        var b = build.Value;
        var flight = Flight.ScheduleNew(b.Customer, b.Station, b.OperationType, b.FlightNumber, b.Schedule, b.AircraftType,
            b.PlannedServices, employees.Value, contractId: null, contractNumber: null,
            createdByUserId: user.UserId ?? Guid.Empty, now: now);
        if (flight.IsFailure)
            return flight.Error;

        db.Flights.Add(flight.Value);

        await timeline.AppendAsync(flight.Value.Id, FlightTimelineEventType.FlightScheduled, now, cancellationToken: cancellationToken);
        foreach (var employee in employees.Value)
            await timeline.AppendAsync(flight.Value.Id, FlightTimelineEventType.EmployeeAssigned, now, details: employee.FullName, cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return flight.Value.Id;
    }
}

// --- Schedule flights --------------------------------------------------------

public sealed record ScheduleFlightsCommand(
    Guid CustomerId,
    Guid StationId,
    Guid OperationTypeId,
    string FlightNumber,
    TimeOnly ScheduledArrivalTimeUtc,
    TimeOnly ScheduledDepartureTimeUtc,
    IReadOnlyList<DateOnly> SelectedDates,
    Guid? AircraftTypeId,
    IReadOnlyList<Guid> PlannedServiceIds,
    IReadOnlyList<Guid> AssignedStaffMemberIds) : ICommand<IReadOnlyList<Guid>>;

public sealed class ScheduleFlightsCommandValidator : AbstractValidator<ScheduleFlightsCommand>
{
    public ScheduleFlightsCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.StationId).NotEmpty();
        RuleFor(x => x.OperationTypeId).NotEmpty();
        RuleFor(x => x.FlightNumber).NotEmpty().MaximumLength(12);
        RuleFor(x => x.PlannedServiceIds).NotEmpty();
        RuleFor(x => x.SelectedDates).NotEmpty();
    }
}

public sealed class ScheduleFlightsCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    MasterDataResolver resolver,
    IFlightTimelineWriter timeline,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<ScheduleFlightsCommand, IReadOnlyList<Guid>>
{
    public async Task<Result<IReadOnlyList<Guid>>> Handle(ScheduleFlightsCommand request, CancellationToken cancellationToken)
    {
        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var stationCheck = scopeResult.Value.EnsureStation(request.StationId);
        if (stationCheck.IsFailure)
            return stationCheck.Error;

        if (request.OperationTypeId == WellKnownMasterDataIds.AdHocOperationType)
            return Error.Validation("Scheduled flights cannot use the Ad Hoc operation type.", "Operations.Flight.AdHocNotSchedulable");

        var selectedDates = request.SelectedDates
            .Distinct()
            .OrderBy(date => date)
            .ToList();
        if (selectedDates.Count == 0)
            return Error.Validation("At least one selected date is required.", "Operations.Flight.BatchDatesRequired");

        var references = await FlightBuildHelpers.BuildReferencesAsync(resolver, request.CustomerId, request.StationId,
            request.OperationTypeId, request.AircraftTypeId, request.FlightNumber, request.PlannedServiceIds, cancellationToken);
        if (references.IsFailure)
            return references.Error;

        if (PerLandingAssignmentGuard.HasPerLandingAssignedStaff(references.Value.PlannedServices, request.AssignedStaffMemberIds))
            return PerLandingAssignmentGuard.Error();

        var employees = await resolver.StaffMembersForStationAsync(request.AssignedStaffMemberIds, request.StationId, cancellationToken);
        if (employees.IsFailure)
            return employees.Error;

        var now = timeProvider.GetUtcNow();
        var ids = new List<Guid>(selectedDates.Count);
        var flights = new List<Flight>(selectedDates.Count);

        foreach (var selectedDate in selectedDates)
        {
            var sta = CombineUtc(selectedDate, request.ScheduledArrivalTimeUtc);
            var std = CombineUtc(selectedDate, request.ScheduledDepartureTimeUtc);
            if (std <= sta)
                std = std.AddDays(1);

            var built = FlightBuildHelpers.BuildWithSchedule(references.Value, sta, std);
            if (built.IsFailure)
                return Error.Validation($"Flight on {selectedDate:yyyy-MM-dd}: {built.Error.Description}", "Operations.Flight.BatchItemInvalid");

            var b = built.Value;
            var flight = Flight.ScheduleNew(b.Customer, b.Station, b.OperationType, b.FlightNumber, b.Schedule, b.AircraftType,
                b.PlannedServices, FlightBuildHelpers.CopyStaffMembers(employees.Value), contractId: null, contractNumber: null,
                createdByUserId: user.UserId ?? Guid.Empty, now: now);
            if (flight.IsFailure)
                return Error.Validation($"Flight on {selectedDate:yyyy-MM-dd}: {flight.Error.Description}", "Operations.Flight.BatchItemInvalid");

            db.Flights.Add(flight.Value);
            flights.Add(flight.Value);
            ids.Add(flight.Value.Id);
        }

        foreach (var flight in flights)
        {
            await timeline.AppendAsync(flight.Id, FlightTimelineEventType.FlightScheduled, now, cancellationToken: cancellationToken);
            foreach (var employee in flight.AssignedEmployees)
                await timeline.AppendAsync(flight.Id, FlightTimelineEventType.EmployeeAssigned, now, details: employee.Employee.FullName, cancellationToken: cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return ids;
    }

    private static DateTimeOffset CombineUtc(DateOnly date, TimeOnly time)
    {
        var dateTime = date.ToDateTime(time, DateTimeKind.Utc);
        return new DateTimeOffset(dateTime);
    }
}

// --- Update scheduled flight -----------------------------------------------

public sealed record UpdateScheduledFlightCommand(
    Guid Id,
    Guid CustomerId,
    Guid StationId,
    Guid OperationTypeId,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    Guid? AircraftTypeId,
    IReadOnlyList<Guid> PlannedServiceIds,
    byte[] RowVersion) : ICommand;

public sealed class UpdateScheduledFlightCommandValidator : AbstractValidator<UpdateScheduledFlightCommand>
{
    public UpdateScheduledFlightCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class UpdateScheduledFlightCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    MasterDataResolver resolver,
    TimeProvider timeProvider) : ICommandHandler<UpdateScheduledFlightCommand>
{
    public async Task<Result> Handle(UpdateScheduledFlightCommand request, CancellationToken cancellationToken)
    {
        var flight = await db.Flights
            .Include(f => f.PlannedServices)
            .Include(f => f.AssignedEmployees)
            .FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var accessCheck = scopeResult.Value.EnsureFlightAccess(flight);
        if (accessCheck.IsFailure)
            return accessCheck.Error;

        var editCheck = flight.EnsureScheduledDetailsEditable();
        if (editCheck.IsFailure)
            return editCheck.Error;

        var aircraft = await resolver.AircraftTypeAsync(request.AircraftTypeId, cancellationToken);
        if (aircraft.IsFailure)
            return aircraft.Error;

        var schedule = ScheduledTime.Create(request.ScheduledArrivalUtc, request.ScheduledDepartureUtc);
        if (schedule.IsFailure)
            return schedule.Error;

        var services = await resolver.ServicesAsync(request.PlannedServiceIds, cancellationToken);
        if (services.IsFailure)
            return services.Error;

        var now = timeProvider.GetUtcNow();
        var updateSchedule = flight.UpdateSchedule(schedule.Value, aircraft.Value, now);
        if (updateSchedule.IsFailure)
            return updateSchedule.Error;

        var updateServices = flight.ReplacePlannedServices(services.Value, now);
        if (updateServices.IsFailure)
            return updateServices.Error;

        db.SetOriginalRowVersion(flight, request.RowVersion);
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

// --- Change flight number ---------------------------------------------------

public sealed record ChangeFlightNumberCommand(Guid Id, string FlightNumber, byte[] RowVersion) : ICommand;

public sealed class ChangeFlightNumberCommandValidator : AbstractValidator<ChangeFlightNumberCommand>
{
    public ChangeFlightNumberCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.FlightNumber).NotEmpty().MaximumLength(12);
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class ChangeFlightNumberCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    TimeProvider timeProvider) : ICommandHandler<ChangeFlightNumberCommand>
{
    public async Task<Result> Handle(ChangeFlightNumberCommand request, CancellationToken cancellationToken)
    {
        var flight = await db.Flights
            .Include(f => f.PlannedServices)
            .Include(f => f.AssignedEmployees)
            .FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var accessCheck = scopeResult.Value.EnsureFlightAccess(flight);
        if (accessCheck.IsFailure)
            return accessCheck.Error;

        var editCheck = flight.EnsureScheduledDetailsEditable();
        if (editCheck.IsFailure)
            return editCheck.Error;

        var number = FlightNumber.Create(request.FlightNumber);
        if (number.IsFailure)
            return number.Error;

        var change = flight.ChangeFlightNumber(number.Value, timeProvider.GetUtcNow());
        if (change.IsFailure)
            return change.Error;

        db.SetOriginalRowVersion(flight, request.RowVersion);
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

// --- Assign employees -------------------------------------------------------

public sealed record AssignEmployeesCommand(Guid FlightId, IReadOnlyList<Guid> StaffMemberIds, byte[] RowVersion) : ICommand;

public sealed class AssignEmployeesCommandValidator : AbstractValidator<AssignEmployeesCommand>
{
    public AssignEmployeesCommandValidator()
    {
        RuleFor(x => x.FlightId).NotEmpty();
        RuleFor(x => x.StaffMemberIds).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class AssignEmployeesCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    MasterDataResolver resolver,
    IFlightTimelineWriter timeline,
    TimeProvider timeProvider) : ICommandHandler<AssignEmployeesCommand>
{
    public async Task<Result> Handle(AssignEmployeesCommand request, CancellationToken cancellationToken)
    {
        var flight = await db.Flights
            .Include(f => f.AssignedEmployees)
            .Include(f => f.PlannedServices)
            .FirstOrDefaultAsync(f => f.Id == request.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        // Admins/schedulers assign freely; a station staff member may invite others only onto a
        // flight they can already access (assigned to it, or a station-wide Per-Landing flight).
        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var accessCheck = scopeResult.Value.EnsureFlightAccess(flight);
        if (accessCheck.IsFailure)
            return accessCheck.Error;

        var editCheck = flight.EnsureScheduledDetailsEditable();
        if (editCheck.IsFailure)
            return editCheck.Error;

        if (flight.IsPerLanding)
            return PerLandingAssignmentGuard.Error();

        var employees = await resolver.StaffMembersForStationAsync(request.StaffMemberIds, flight.Station.StationId, cancellationToken);
        if (employees.IsFailure)
            return employees.Error;

        var now = timeProvider.GetUtcNow();
        var alreadyAssigned = flight.AssignedEmployees.Select(e => e.Employee.StaffMemberId).ToHashSet();

        db.SetOriginalRowVersion(flight, request.RowVersion);
        var assign = flight.AssignEmployees(employees.Value, now);
        if (assign.IsFailure)
            return assign.Error;

        foreach (var employee in employees.Value.Where(e => !alreadyAssigned.Contains(e.StaffMemberId)))
            await timeline.AppendAsync(flight.Id, FlightTimelineEventType.EmployeeAssigned, now, details: employee.FullName, cancellationToken: cancellationToken);

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

// --- Invite/forward employees onto a scheduled non-Per-Landing flight -------

public sealed record InviteEmployeesToFlightCommand(Guid FlightId, IReadOnlyList<Guid> StaffMemberIds, byte[] RowVersion) : ICommand;

public sealed class InviteEmployeesToFlightCommandValidator : AbstractValidator<InviteEmployeesToFlightCommand>
{
    public InviteEmployeesToFlightCommandValidator()
    {
        RuleFor(x => x.FlightId).NotEmpty();
        RuleFor(x => x.StaffMemberIds).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class InviteEmployeesToFlightCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    MasterDataResolver resolver,
    IFlightTimelineWriter timeline,
    TimeProvider timeProvider) : ICommandHandler<InviteEmployeesToFlightCommand>
{
    public async Task<Result> Handle(InviteEmployeesToFlightCommand request, CancellationToken cancellationToken)
    {
        var flight = await db.Flights
            .Include(f => f.AssignedEmployees)
            .Include(f => f.PlannedServices)
            .FirstOrDefaultAsync(f => f.Id == request.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var accessCheck = scopeResult.Value.EnsureFlightAccess(flight);
        if (accessCheck.IsFailure)
            return accessCheck.Error;

        var editCheck = flight.EnsureScheduledDetailsEditable();
        if (editCheck.IsFailure)
            return editCheck.Error;

        if (flight.IsPerLanding)
            return PerLandingAssignmentGuard.Error();

        var requested = request.StaffMemberIds.Distinct().ToList();
        var alreadyAssigned = flight.AssignedEmployees.Select(e => e.Employee.StaffMemberId).ToHashSet();
        if (requested.Any(alreadyAssigned.Contains))
            return Error.Conflict("One or more selected employees are already assigned to this flight.", "Operations.Flight.AssignmentAlreadyExists");

        var employees = await resolver.StaffMembersForStationAsync(requested, flight.Station.StationId, cancellationToken);
        if (employees.IsFailure)
            return employees.Error;

        var now = timeProvider.GetUtcNow();
        db.SetOriginalRowVersion(flight, request.RowVersion);
        var assign = flight.AssignEmployees(employees.Value, now);
        if (assign.IsFailure)
            return assign.Error;

        foreach (var employee in employees.Value)
            await timeline.AppendAsync(flight.Id, FlightTimelineEventType.EmployeeAssigned, now, details: employee.FullName, cancellationToken: cancellationToken);

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

internal static class PerLandingAssignmentGuard
{
    public static bool HasPerLandingAssignedStaff(
        IReadOnlyList<ServiceSnapshot> plannedServices,
        IReadOnlyCollection<Guid> assignedStaffMemberIds) =>
        assignedStaffMemberIds.Count > 0 &&
        plannedServices.Any(service => PerLandingPolicy.IsAircraftPerLanding(service.ServiceId));

    public static Error Error() =>
        BuildingBlocks.Domain.Results.Error.Validation(
            "Per Landing flights cannot have assigned staff because they are available station-wide.",
            "Operations.PerLanding.AssignmentNotAllowed");
}
