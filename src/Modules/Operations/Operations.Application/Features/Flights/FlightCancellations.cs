using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Mobile;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Contracts;
using Operations.Domain.ValueObjects;

namespace Operations.Application.Features.Flights;

public sealed record SaveFlightCancellationCommand(Guid FlightId, SaveFlightCancellationRequest Cancellation, byte[] RowVersion)
    : ICommand<FlightCancellationSavedDto>;

public sealed class SaveFlightCancellationCommandValidator : AbstractValidator<SaveFlightCancellationCommand>
{
    public SaveFlightCancellationCommandValidator()
    {
        RuleFor(command => command.FlightId).NotEmpty();
        RuleFor(command => command.RowVersion).Must(version => version is { Length: 8 });
        RuleFor(command => command.Cancellation).NotNull();
        When(command => command.Cancellation is not null, () => RuleFor(command => command.Cancellation.CanceledAtUtc).NotEmpty());
    }
}

public sealed class SaveFlightCancellationCommandHandler(
    IOperationsDbContext db,
    IMobileFlightScope scope,
    MasterDataResolver resolver,
    TimeProvider clock,
    IMobileSyncBroadcaster mobileSync) : ICommandHandler<SaveFlightCancellationCommand, FlightCancellationSavedDto>
{
    public async Task<Result<FlightCancellationSavedDto>> Handle(SaveFlightCancellationCommand request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure) return resolved.Error;
        var actor = resolved.Value;
        var flight = await MobileFlightQuery.ApplyReadScope(db.Flights, actor).AsSplitQuery()
            .Include(f => f.PlannedServices).Include(f => f.AssignedEmployees)
            .Include(f => f.ArrivalSubmissions).Include(f => f.DepartureSubmissions)
            .Include(f => f.CancellationSubmissions).Include(f => f.ReturnToRamps)
            .Include(f => f.ServiceRecords).ThenInclude(service => service.Performers)
            .Include(f => f.ServiceRecords).ThenInclude(service => service.Attachments)
            .Include(f => f.TaskRecords).ThenInclude(task => task.Tools)
            .Include(f => f.TaskRecords).ThenInclude(task => task.Materials)
            .Include(f => f.TaskRecords).ThenInclude(task => task.GeneralSupports)
            .Include(f => f.TaskRecords).ThenInclude(task => task.Performers)
            .Include(f => f.TaskRecords).ThenInclude(task => task.Attachments)
            .SingleOrDefaultAsync(f => f.Id == request.FlightId, cancellationToken);
        if (flight is null) return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");
        if (flight.ArrivalSubmissions.Any(a => a.SubmittedByEmployee.StaffMemberId == actor.StaffMemberId) ||
            flight.DepartureSubmissions.Any(d => d.SubmittedByEmployee.StaffMemberId == actor.StaffMemberId))
            return Error.Conflict("You already submitted work details for this flight and cannot submit cancellation.",
                "Operations.Flight.OwnWorkSubmitted");
        var own = flight.CancellationSubmissions.SingleOrDefault(c => c.SubmittedByEmployee.StaffMemberId == actor.StaffMemberId);
        if (FlightSearchPolicy.IsUnassignedRegular(flight, actor.StaffMemberId) &&
            !flight.CancellationSubmissions.Any(item => item.SubmittedByEmployee.StaffMemberId == actor.StaffMemberId))
            return Error.Forbidden("Cancellation is read-only on this unassigned flight.", "Operations.Search.CancellationReadOnly");
        var input = request.Cancellation;
        var details = CancellationDetails.Create(input.CanceledAtUtc, input.Remarks);
        if (details.IsFailure) return details.Error;
        // A lost successful save can be acknowledged without repeating a write or a warning.
        if (own is not null && own.Details == details.Value)
            return new FlightCancellationSavedDto(FlightCancellationSubmissionDto.Map(own), Convert.ToBase64String(flight.RowVersion));
        if (!flight.RowVersion.SequenceEqual(request.RowVersion)) return ConcurrencyErrors.Stale;
        if ((flight.ArrivalSubmissions.Count > 0 || flight.DepartureSubmissions.Count > 0) &&
            !input.AcknowledgeConflictingSubmission)
            return Error.Conflict("Another employee reported arrival or departure for this flight. Confirm before saving cancellation.",
                "Operations.Flight.WorkConfirmationRequired");
        var staff = await resolver.StaffMembersForStationAsync([actor.StaffMemberId], flight.Station.StationId, cancellationToken);
        if (staff.IsFailure) return staff.Error;
        db.SetOriginalRowVersion(flight, request.RowVersion);
        var submitted = flight.SubmitCancellation(staff.Value.Single(), details.Value, actor.UserId, clock.GetUtcNow());
        if (submitted.IsFailure) return submitted.Error;
        await FlightActivityRecorder.TouchAsync(db, flight, actor, clock.GetUtcNow(), cancellationToken);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return ConcurrencyErrors.Stale; }
        catch (DbUpdateException) when (own is null)
        {
            if (await db.Flights.AsNoTracking().AnyAsync(f => f.Id == request.FlightId && f.CancellationSubmissions.Any(), cancellationToken))
                return ConcurrencyErrors.Stale;
            throw;
        }
        mobileSync.Enqueue(new MobileSyncChange(MobileSyncTables.Flights, MobileSyncOps.Refresh, null,
            MobileSyncAudience.AllStations, clock.GetUtcNow()));
        return new FlightCancellationSavedDto(FlightCancellationSubmissionDto.Map(submitted.Value), Convert.ToBase64String(flight.RowVersion));
    }
}

public sealed record RevertFlightCancellationCommand(Guid FlightId, Guid SubmissionId, byte[] RowVersion)
    : ICommand<FlightCancellationRevertedDto>;

public sealed class RevertFlightCancellationCommandValidator : AbstractValidator<RevertFlightCancellationCommand>
{
    public RevertFlightCancellationCommandValidator()
    {
        RuleFor(command => command.FlightId).NotEmpty();
        RuleFor(command => command.SubmissionId).NotEmpty();
        RuleFor(command => command.RowVersion).Must(version => version is { Length: 8 });
    }
}

public sealed class RevertFlightCancellationCommandHandler(
    IOperationsDbContext db,
    IMobileFlightScope scope,
    TimeProvider clock,
    IMobileSyncBroadcaster mobileSync) : ICommandHandler<RevertFlightCancellationCommand, FlightCancellationRevertedDto>
{
    public async Task<Result<FlightCancellationRevertedDto>> Handle(RevertFlightCancellationCommand request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure) return resolved.Error;
        var flight = await MobileFlightQuery.ApplyReadScope(db.Flights, resolved.Value)
            .Include(f => f.CancellationSubmissions).Include(f => f.PlannedServices).Include(f => f.AssignedEmployees)
            .SingleOrDefaultAsync(f => f.Id == request.FlightId, cancellationToken);
        if (flight is null) return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");
        var own = flight.CancellationSubmissions.SingleOrDefault(c => c.SubmittedByEmployee.StaffMemberId == resolved.Value.StaffMemberId);
        if (own?.Id == request.SubmissionId && !flight.RowVersion.SequenceEqual(request.RowVersion))
            return ConcurrencyErrors.Stale;
        // The submission identity prevents a delayed revert from removing a newer replacement.
        var reverted = flight.RevertCancellation(resolved.Value.StaffMemberId, request.SubmissionId, clock.GetUtcNow());
        if (reverted.IsFailure) return reverted.Error;
        if (own is not null)
        {
            db.SetOriginalRowVersion(flight, request.RowVersion);
            await FlightActivityRecorder.TouchAsync(db, flight, resolved.Value, clock.GetUtcNow(), cancellationToken);
            try { await db.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return ConcurrencyErrors.Stale; }
            mobileSync.Enqueue(new MobileSyncChange(MobileSyncTables.Flights, MobileSyncOps.Refresh, null,
                MobileSyncAudience.AllStations, clock.GetUtcNow()));
        }
        return new FlightCancellationRevertedDto(flight.CancellationSubmissions.OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id)
            .Select(FlightCancellationSubmissionDto.Map).ToList(), Convert.ToBase64String(flight.RowVersion));
    }
}
