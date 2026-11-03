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
using Operations.Domain.Authorization;
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
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<ScheduleFlightCommand, Guid>
{
    public async Task<Result<Guid>> Handle(ScheduleFlightCommand request, CancellationToken cancellationToken)
    {
        if (request.AssignedStaffMemberIds is { Count: > 0 } &&
            !user.HasPermission(OperationsPermissions.Flights.Assign))
        {
            return Error.Forbidden(
                "Scheduling a flight with assigned staff requires flight assignment permission.",
                "Operations.Flight.AssignForbidden");
        }

        var scopeResult = await scope.ResolveForWriteAsync(cancellationToken);
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
            b.PlannedServices, employees.Value,
            createdByUserId: user.UserId ?? Guid.Empty, now: now);
        if (flight.IsFailure)
            return flight.Error;

        db.Flights.Add(flight.Value);

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
    IReadOnlyList<Guid> AssignedStaffMemberIds,
    string? TimeZoneId = null) : ICommand<IReadOnlyList<Guid>>;

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
        RuleFor(x => x.TimeZoneId).MaximumLength(128);
    }
}

public sealed class ScheduleFlightsCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    MasterDataResolver resolver,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<ScheduleFlightsCommand, IReadOnlyList<Guid>>
{
    public async Task<Result<IReadOnlyList<Guid>>> Handle(ScheduleFlightsCommand request, CancellationToken cancellationToken)
    {
        if (request.AssignedStaffMemberIds is { Count: > 0 } &&
            !user.HasPermission(OperationsPermissions.Flights.Assign))
        {
            return Error.Forbidden(
                "Scheduling flights with assigned staff requires flight assignment permission.",
                "Operations.Flight.AssignForbidden");
        }

        var scopeResult = await scope.ResolveForWriteAsync(cancellationToken);
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

        if (!TryResolveTimeZone(request.TimeZoneId, out var scheduleTimeZone))
        {
            return Error.Validation(
                $"Time zone '{request.TimeZoneId}' is not recognized. Refresh the page and try again.",
                "Operations.Flight.BatchTimeZoneInvalid");
        }

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

        foreach (var selectedDate in selectedDates)
        {
            var departureDate = request.ScheduledDepartureTimeUtc <= request.ScheduledArrivalTimeUtc
                ? selectedDate.AddDays(1)
                : selectedDate;
            if (!TryCombineUtc(selectedDate, request.ScheduledArrivalTimeUtc, scheduleTimeZone, out var sta, out var arrivalError))
            {
                return Error.Validation(
                    $"Flight on {selectedDate:yyyy-MM-dd}: STA {arrivalError}",
                    "Operations.Flight.BatchLocalTimeInvalid");
            }
            if (!TryCombineUtc(departureDate, request.ScheduledDepartureTimeUtc, scheduleTimeZone, out var std, out var departureError))
            {
                return Error.Validation(
                    $"Flight on {selectedDate:yyyy-MM-dd}: STD {departureError}",
                    "Operations.Flight.BatchLocalTimeInvalid");
            }

            var built = FlightBuildHelpers.BuildWithSchedule(references.Value, sta, std);
            if (built.IsFailure)
                return Error.Validation($"Flight on {selectedDate:yyyy-MM-dd}: {built.Error.Description}", "Operations.Flight.BatchItemInvalid");

            var b = built.Value;
            var flight = Flight.ScheduleNew(b.Customer, b.Station, b.OperationType, b.FlightNumber, b.Schedule, b.AircraftType,
                b.PlannedServices, FlightBuildHelpers.CopyStaffMembers(employees.Value),
                createdByUserId: user.UserId ?? Guid.Empty, now: now);
            if (flight.IsFailure)
                return Error.Validation($"Flight on {selectedDate:yyyy-MM-dd}: {flight.Error.Description}", "Operations.Flight.BatchItemInvalid");

            db.Flights.Add(flight.Value);
            ids.Add(flight.Value.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
        return ids;
    }

    internal static bool TryResolveTimeZone(string? timeZoneId, out TimeZoneInfo timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            timeZone = TimeZoneInfo.Utc;
            return true;
        }

        const string browserOffsetPrefix = "Browser UTC";
        var rawBrowserOffset = timeZoneId.StartsWith(browserOffsetPrefix, StringComparison.Ordinal)
            ? timeZoneId[browserOffsetPrefix.Length..]
            : string.Empty;
        if (rawBrowserOffset.Length > 1 &&
            rawBrowserOffset[0] is '+' or '-' &&
            TimeSpan.TryParse(rawBrowserOffset[1..], out var browserOffsetMagnitude))
        {
            var browserOffset = rawBrowserOffset[0] == '-' ? -browserOffsetMagnitude : browserOffsetMagnitude;
            if (browserOffset >= TimeSpan.FromHours(-14) && browserOffset <= TimeSpan.FromHours(14))
            {
                timeZone = TimeZoneInfo.CreateCustomTimeZone(timeZoneId, browserOffset, timeZoneId, timeZoneId);
                return true;
            }
        }

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var windowsId))
            {
                try
                {
                    timeZone = TimeZoneInfo.FindSystemTimeZoneById(windowsId);
                    return true;
                }
                catch (TimeZoneNotFoundException)
                {
                    // Return a validation error below.
                }
                catch (InvalidTimeZoneException)
                {
                    // Return a validation error below.
                }
            }
        }
        catch (InvalidTimeZoneException)
        {
            // Return a validation error below.
        }

        timeZone = TimeZoneInfo.Utc;
        return false;
    }

    internal static bool TryCombineUtc(
        DateOnly date,
        TimeOnly time,
        TimeZoneInfo timeZone,
        out DateTimeOffset instant,
        out string error)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(local))
        {
            instant = default;
            error = $"{local:HH:mm} does not exist in {timeZone.Id} because the clock moves forward.";
            return false;
        }
        if (timeZone.IsAmbiguousTime(local))
        {
            instant = default;
            error = $"{local:HH:mm} occurs twice in {timeZone.Id} because the clock moves back; choose a time outside the repeated hour.";
            return false;
        }

        instant = new DateTimeOffset(local, timeZone.GetUtcOffset(local)).ToUniversalTime();
        error = string.Empty;
        return true;
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
    byte[] RowVersion,
    IReadOnlyList<Guid>? AssignedStaffMemberIds = null) : ICommand;

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
    IUserContext user,
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

        var scopeResult = await scope.ResolveForWriteAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var accessCheck = scopeResult.Value.EnsureFlightAccess(flight);
        if (accessCheck.IsFailure)
            return accessCheck.Error;

        var aircraft = await resolver.AircraftTypeAsync(request.AircraftTypeId, cancellationToken);
        if (aircraft.IsFailure)
            return aircraft.Error;

        var schedule = ScheduledTime.Create(request.ScheduledArrivalUtc, request.ScheduledDepartureUtc);
        if (schedule.IsFailure)
            return schedule.Error;

        var services = await resolver.ServicesAsync(request.PlannedServiceIds, cancellationToken);
        if (services.IsFailure)
            return services.Error;

        IReadOnlyList<StaffMemberSnapshot>? employees = null;
        if (request.AssignedStaffMemberIds is { } staffIds)
        {
            var currentIds = flight.AssignedEmployees.Select(employee => employee.Employee.StaffMemberId).ToHashSet();
            if (!currentIds.SetEquals(staffIds))
            {
                if (!user.HasPermission(OperationsPermissions.Flights.Assign))
                    return Error.Forbidden(
                        "Changing assigned staff requires flight assignment permission.",
                        "Operations.Flight.AssignForbidden");

                var resolved = await resolver.StaffMembersForStationAsync(staffIds, flight.Station.StationId, cancellationToken);
                if (resolved.IsFailure)
                    return resolved.Error;
                employees = resolved.Value;
            }
        }

        var update = flight.UpdatePlanning(schedule.Value, aircraft.Value, services.Value, employees, timeProvider.GetUtcNow());
        if (update.IsFailure)
            return update.Error;

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

        var scopeResult = await scope.ResolveForWriteAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var accessCheck = scopeResult.Value.EnsureFlightAccess(flight);
        if (accessCheck.IsFailure)
            return accessCheck.Error;

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
        RuleFor(x => x.StaffMemberIds).NotNull();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class AssignEmployeesCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    MasterDataResolver resolver,
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

        var scopeResult = await scope.ResolveForWriteAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var accessCheck = scopeResult.Value.EnsureFlightAccess(flight);
        if (accessCheck.IsFailure)
            return accessCheck.Error;

        if (flight.IsPerLanding)
            return PerLandingAssignmentGuard.Error();

        var employees = await resolver.StaffMembersForStationAsync(request.StaffMemberIds, flight.Station.StationId, cancellationToken);
        if (employees.IsFailure)
            return employees.Error;

        var now = timeProvider.GetUtcNow();

        db.SetOriginalRowVersion(flight, request.RowVersion);
        var assign = flight.ReplaceAssignedEmployees(employees.Value, now);
        if (assign.IsFailure)
            return assign.Error;

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
