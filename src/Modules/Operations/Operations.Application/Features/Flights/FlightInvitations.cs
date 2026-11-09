using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Mobile;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Contracts.Readers;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Contracts;
using Operations.Contracts;
using Operations.Domain.Flights;

namespace Operations.Application.Features.Flights;

public sealed record GetFlightInvitationOptionsQuery(Guid FlightId, bool MobileOnly = false) : IQuery<IReadOnlyList<AssignedEmployeeDto>>;

public sealed class GetFlightInvitationOptionsQueryHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IMasterDataReader masterData,
    IMobileFlightScope mobileScope)
    : IQueryHandler<GetFlightInvitationOptionsQuery, IReadOnlyList<AssignedEmployeeDto>>
{
    public async Task<Result<IReadOnlyList<AssignedEmployeeDto>>> Handle(
        GetFlightInvitationOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Flights.AsNoTracking();
        if (request.MobileOnly)
        {
            var mobile = await mobileScope.ResolveAsync(cancellationToken);
            if (mobile.IsFailure)
                return mobile.Error;
            query = MobileFlightQuery.ApplyScope(query, mobile.Value);
        }

        var flight = await query
            .Include(flight => flight.PlannedServices)
            .Include(flight => flight.AssignedEmployees)
            .FirstOrDefaultAsync(flight => flight.Id == request.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        if (!request.MobileOnly)
        {
            var resolvedScope = await scope.ResolveForWriteAsync(cancellationToken);
            if (resolvedScope.IsFailure)
                return resolvedScope.Error;
            var access = resolvedScope.Value.EnsureFlightAccess(flight);
            if (access.IsFailure)
                return access.Error;
        }
        if (flight.IsPerLanding)
            return PerLandingAssignmentGuard.Error();

        var assigned = flight.AssignedEmployees.Select(employee => employee.Employee.StaffMemberId).ToHashSet();
        var staff = await masterData.GetActiveStaffMembersForStationAsync(flight.Station.StationId, cancellationToken);
        return staff
            .Where(member => member.IsActive && member.StationId == flight.Station.StationId && !assigned.Contains(member.Id))
            .OrderBy(member => member.FullName)
            .Select(member => new AssignedEmployeeDto(member.Id, member.FullName, member.EmployeeId))
            .ToList();
    }
}

public sealed record InviteEmployeesToFlightCommand(
    Guid FlightId,
    IReadOnlyList<Guid> StaffMemberIds,
    byte[] RowVersion,
    bool MobileOnly = false) : ICommand;

public sealed class InviteEmployeesToFlightCommandValidator : AbstractValidator<InviteEmployeesToFlightCommand>
{
    public InviteEmployeesToFlightCommandValidator()
    {
        RuleFor(command => command.FlightId).NotEmpty();
        RuleFor(command => command.StaffMemberIds).NotEmpty();
        RuleFor(command => command.RowVersion).NotEmpty();
    }
}

public sealed class InviteEmployeesToFlightCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    MasterDataResolver resolver,
    IUserContext user,
    IAuditContext auditContext,
    TimeProvider timeProvider,
    IMobileSyncBroadcaster mobileSync,
    IMobileFlightScope mobileScope) : ICommandHandler<InviteEmployeesToFlightCommand>
{
    public async Task<Result> Handle(InviteEmployeesToFlightCommand request, CancellationToken cancellationToken)
    {
        IQueryable<Flight> query = db.Flights;
        if (request.MobileOnly)
        {
            var mobile = await mobileScope.ResolveAsync(cancellationToken);
            if (mobile.IsFailure)
                return mobile.Error;
            query = MobileFlightQuery.ApplyScope(query, mobile.Value);
        }

        var flight = await query
            .Include(flight => flight.PlannedServices)
            .Include(flight => flight.AssignedEmployees)
            .FirstOrDefaultAsync(flight => flight.Id == request.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        if (!request.MobileOnly)
        {
            var resolvedScope = await scope.ResolveForWriteAsync(cancellationToken);
            if (resolvedScope.IsFailure)
                return resolvedScope.Error;
            var access = resolvedScope.Value.EnsureFlightAccess(flight);
            if (access.IsFailure)
                return access.Error;
        }
        if (flight.IsPerLanding)
            return PerLandingAssignmentGuard.Error();

        var assigned = flight.AssignedEmployees.Select(employee => employee.Employee.StaffMemberId).ToHashSet();
        var requested = request.StaffMemberIds.Distinct().Where(id => !assigned.Contains(id)).ToList();
        if (requested.Count == 0)
            return Result.Success();

        var employees = await resolver.StaffMembersForStationAsync(requested, flight.Station.StationId, cancellationToken);
        if (employees.IsFailure)
            return employees.Error;

        var now = timeProvider.GetUtcNow();
        db.SetOriginalRowVersion(flight, request.RowVersion);
        var invite = flight.InviteEmployees(employees.Value, now);
        if (invite.IsFailure)
            return invite.Error;

        FlightAssignmentEvents.Enqueue(db, flight,
            employees.Value.Select(employee => employee.StaffMemberId), user, auditContext, now, FlightAssignmentSource.Invite);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            mobileSync.Enqueue(new MobileSyncChange(MobileSyncTables.Flights, MobileSyncOps.Refresh, null, MobileSyncAudience.AllStations, timeProvider.GetUtcNow()));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }

        return Result.Success();
    }
}
