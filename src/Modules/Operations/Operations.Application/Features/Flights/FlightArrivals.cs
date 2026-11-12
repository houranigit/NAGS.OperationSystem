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
            RuleFor(command => command.Arrival.TargetSubmissionId).Must(id => id is null || id != Guid.Empty);
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

        var input = request.Arrival;
        if (input.TargetSubmissionId == Guid.Empty)
            return Error.Validation("Target arrival submission is required.", "Operations.ArrivalSubmission.TargetRequired");
        var existing = input.TargetSubmissionId is { } targetId
            ? flight.ArrivalSubmissions.SingleOrDefault(a => a.Id == targetId)
            : flight.ArrivalSubmissions.SingleOrDefault(a => a.SubmittedByEmployee.StaffMemberId == actor.StaffMemberId);
        if (input.TargetSubmissionId.HasValue && existing is null)
            return Error.NotFound("Arrival submission not found.", "Operations.ArrivalSubmission.NotFound");
        if (existing is null && flight.ArrivalSubmissions.Count == 1)
            existing = flight.ArrivalSubmissions.Single();
        if (existing is null && flight.ArrivalSubmissions.Count > 1)
            return Error.Conflict("Select the arrival submission to update.", "Operations.ArrivalSubmission.TargetRequired");
        var foreign = existing is not null && existing.SubmittedByEmployee.StaffMemberId != actor.StaffMemberId;
        if (foreign && !flight.AssignedEmployees.Any(employee => employee.Employee.StaffMemberId == actor.StaffMemberId))
            return Error.Forbidden("Only assigned employees can update another employee's arrival.", "Operations.ArrivalSubmission.AssignmentRequired");

        // A lost success response may be replayed with the previous rowversion. Identical
        // input is acknowledged without a write, even if unrelated flight data has changed.
        if (existing is not null && existing.Details.Ata == input.Ata &&
            existing.Details.AircraftType.AircraftTypeId == input.AircraftTypeId &&
            existing.Details.AircraftTailNumber == input.AircraftTailNumber.Trim() &&
            existing.Details.CustomerWorkReferenceNumber == Normalize(input.CustomerWorkReferenceNumber) &&
            existing.Details.Remarks == Normalize(input.Remarks) &&
            (foreign || existing.Details.ReceivedByEmployee.StaffMemberId == actor.StaffMemberId))
            return new FlightArrivalSavedDto(FlightArrivalSubmissionDto.Map(existing), Convert.ToBase64String(flight.RowVersion));

        if (!flight.RowVersion.SequenceEqual(request.RowVersion))
            return ConcurrencyErrors.Stale;
        if (flight.CancellationSubmissions.Any(c => c.SubmittedByEmployee.StaffMemberId != actor.StaffMemberId) &&
            !input.AcknowledgeConflictingSubmission)
            return Error.Conflict("Another employee reported this flight as canceled. Confirm before saving work details.",
                "Operations.Flight.CancellationConfirmationRequired");
        if (existing is null || existing.Details.Ata != input.Ata)
        {
            var actualsStaffId = existing?.SubmittedByEmployee.StaffMemberId ?? actor.StaffMemberId;
            var workBounds = FlightServiceBounds.ValidateExistingServices(flight, actualsStaffId, arrivalOverride: input.Ata);
            if (workBounds.IsFailure) return workBounds.Error;
            var taskBounds = FlightTaskBounds.ValidateExistingTasks(flight, actualsStaffId, arrivalOverride: input.Ata);
            if (taskBounds.IsFailure) return taskBounds.Error;
        }
        var aircraft = await resolver.AircraftTypeAsync(input.AircraftTypeId, cancellationToken);
        if (aircraft.IsFailure)
            return aircraft.Error;
        var staff = await resolver.StaffMembersForStationAsync([actor.StaffMemberId], flight.Station.StationId, cancellationToken);
        if (staff.IsFailure)
            return staff.Error;
        var employee = staff.Value.Single();
        var details = ArrivalDetails.Create(input.CustomerWorkReferenceNumber, input.Ata, aircraft.Value,
            input.AircraftTailNumber, foreign ? existing!.Details.ReceivedByEmployee : employee, input.Remarks);
        if (details.IsFailure)
            return details.Error;
        db.SetOriginalRowVersion(flight, request.RowVersion);
        var previousDetails = existing?.Details;
        var now = clock.GetUtcNow();
        var submitted = flight.SubmitArrival(employee, details.Value, actor.UserId, now, existing?.Id);
        if (submitted.IsFailure)
            return submitted.Error;
        if (foreign && previousDetails != submitted.Value.Details)
            db.Enqueue(new global::Operations.Contracts.FlightSubmissionUpdated
            {
                FlightId = flight.Id, FlightNumber = flight.FlightNumber.Value, SubmissionId = submitted.Value.Id,
                SubmissionKind = global::Operations.Contracts.FlightSubmissionKind.Arrival,
                SubmittedByStaffMemberId = submitted.Value.SubmittedByEmployee.StaffMemberId,
                SubmittedByUserId = submitted.Value.CreatedByUserId,
                UpdatedByStaffMemberId = actor.StaffMemberId, UpdatedByUserId = actor.UserId,
                UpdatedByDisplayName = employee.FullName, OccurredOnUtc = now
            });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }
        catch (DbUpdateException) when (existing is null)
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
