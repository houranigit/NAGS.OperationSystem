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

public sealed record SaveFlightArrivalCommand(Guid FlightId, SaveFlightArrivalRequest Arrival, byte[] RowVersion)
    : ICommand<FlightArrivalSavedDto>;

public sealed class SaveFlightArrivalCommandValidator : AbstractValidator<SaveFlightArrivalCommand>
{
    public SaveFlightArrivalCommandValidator()
    {
        RuleFor(command => command.FlightId).NotEmpty();
        RuleFor(command => command.RowVersion).Must(version => version is { Length: 8 });
        RuleFor(command => command.Arrival).NotNull();
        When(command => command.Arrival is not null, () =>
        {
            RuleFor(command => command.Arrival.Ata).NotEmpty();
            RuleFor(command => command.Arrival.AircraftTypeId).NotEmpty();
            RuleFor(command => command.Arrival.AircraftTailNumber).NotEmpty();
        });
    }
}

public sealed class SaveFlightArrivalCommandHandler(
    IOperationsDbContext db,
    IMobileFlightScope scope,
    MasterDataResolver resolver,
    TimeProvider clock,
    IMobileSyncBroadcaster mobileSync) : ICommandHandler<SaveFlightArrivalCommand, FlightArrivalSavedDto>
{
    public async Task<Result<FlightArrivalSavedDto>> Handle(SaveFlightArrivalCommand request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure)
            return resolved.Error;
        var actor = resolved.Value;
        var flight = await MobileFlightQuery.ApplyScope(db.Flights, actor).AsSplitQuery()
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
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");
        if (flight.CancellationSubmissions.Any(c => c.SubmittedByEmployee.StaffMemberId == actor.StaffMemberId))
            return Error.Conflict("Revert your cancellation before submitting arrival details.", "Operations.Flight.OwnCancellationSubmitted");

        var own = flight.ArrivalSubmissions.SingleOrDefault(a => a.SubmittedByEmployee.StaffMemberId == actor.StaffMemberId);
        // Competing input remains a later application workflow; domain history is preserved.
        if (own is null && flight.ArrivalSubmissions.Count > 0)
            return Error.Forbidden("Only the employee who submitted the arrival can update it.", "Operations.Arrival.ReadOnly");

        var input = request.Arrival;
        // A lost success response may be replayed with the previous rowversion. Identical
        // input is acknowledged without a write, even if unrelated flight data has changed.
        if (own is not null && own.Details.Ata == input.Ata &&
            own.Details.AircraftType.AircraftTypeId == input.AircraftTypeId &&
            own.Details.AircraftTailNumber == input.AircraftTailNumber.Trim() &&
            own.Details.CustomerWorkReferenceNumber == Normalize(input.CustomerWorkReferenceNumber) &&
            own.Details.Remarks == Normalize(input.Remarks) &&
            own.Details.ReceivedByEmployee.StaffMemberId == actor.StaffMemberId)
            return new FlightArrivalSavedDto(FlightArrivalSubmissionDto.Map(own), Convert.ToBase64String(flight.RowVersion));

        if (!flight.RowVersion.SequenceEqual(request.RowVersion))
            return ConcurrencyErrors.Stale;
        if (flight.CancellationSubmissions.Any(c => c.SubmittedByEmployee.StaffMemberId != actor.StaffMemberId) &&
            !input.AcknowledgeConflictingSubmission)
            return Error.Conflict("Another employee reported this flight as canceled. Confirm before saving work details.",
                "Operations.Flight.CancellationConfirmationRequired");
        var aircraft = await resolver.AircraftTypeAsync(input.AircraftTypeId, cancellationToken);
        if (aircraft.IsFailure)
            return aircraft.Error;
        var staff = await resolver.StaffMembersForStationAsync([actor.StaffMemberId], flight.Station.StationId, cancellationToken);
        if (staff.IsFailure)
            return staff.Error;
        var employee = staff.Value.Single();
        var details = ArrivalDetails.Create(input.CustomerWorkReferenceNumber, input.Ata, aircraft.Value,
            input.AircraftTailNumber, employee, input.Remarks);
        if (details.IsFailure)
            return details.Error;
        db.SetOriginalRowVersion(flight, request.RowVersion);
        var submitted = flight.SubmitArrival(employee, details.Value, actor.UserId, clock.GetUtcNow());
        if (submitted.IsFailure)
            return submitted.Error;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }
        catch (DbUpdateException) when (own is null)
        {
            // Concurrent first submissions can hit the unique accepted/submitting-staff
            // index before EF issues the aggregate rowversion update. Preserve the same
            // reviewable conflict response; unrelated persistence failures still surface.
            if (await db.Flights.AsNoTracking().AnyAsync(f => f.Id == request.FlightId && f.ArrivalSubmissions.Any(), cancellationToken))
                return ConcurrencyErrors.Stale;
            throw;
        }
        mobileSync.Enqueue(new MobileSyncChange(MobileSyncTables.Flights, MobileSyncOps.Refresh, null,
            MobileSyncAudience.AllStations, clock.GetUtcNow()));
        return new FlightArrivalSavedDto(FlightArrivalSubmissionDto.Map(submitted.Value), Convert.ToBase64String(flight.RowVersion));
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
