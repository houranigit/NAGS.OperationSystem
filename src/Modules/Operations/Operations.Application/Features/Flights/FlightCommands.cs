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

        var employees = await resolver.StaffMembersAsync(request.AssignedStaffMemberIds, cancellationToken);
        if (employees.IsFailure)
            return employees.Error;

        var b = build.Value;
        var flight = Flight.ScheduleNew(b.Customer, b.Station, b.OperationType, b.FlightNumber, b.Schedule, b.AircraftType,
            b.PlannedServices, employees.Value, contractId: null, contractNumber: null,
            createdByUserId: user.UserId ?? Guid.Empty, now: timeProvider.GetUtcNow());
        if (flight.IsFailure)
            return flight.Error;

        db.Flights.Add(flight.Value);
        await db.SaveChangesAsync(cancellationToken);
        return flight.Value.Id;
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
            .FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var stationCheck = scopeResult.Value.EnsureStation(flight.Station.StationId);
        if (stationCheck.IsFailure)
            return stationCheck.Error;

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
        var flight = await db.Flights.FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var stationCheck = scopeResult.Value.EnsureStation(flight.Station.StationId);
        if (stationCheck.IsFailure)
            return stationCheck.Error;

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
    TimeProvider timeProvider) : ICommandHandler<AssignEmployeesCommand>
{
    public async Task<Result> Handle(AssignEmployeesCommand request, CancellationToken cancellationToken)
    {
        var flight = await db.Flights
            .Include(f => f.AssignedEmployees)
            .FirstOrDefaultAsync(f => f.Id == request.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var stationCheck = scopeResult.Value.EnsureStation(flight.Station.StationId);
        if (stationCheck.IsFailure)
            return stationCheck.Error;

        var employees = await resolver.StaffMembersAsync(request.StaffMemberIds, cancellationToken);
        if (employees.IsFailure)
            return employees.Error;

        var assign = flight.AssignEmployees(employees.Value, timeProvider.GetUtcNow());
        if (assign.IsFailure)
            return assign.Error;

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
