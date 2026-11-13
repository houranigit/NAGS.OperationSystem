using System.Buffers.Binary;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Mobile;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Contracts;
using Operations.Domain.ValueObjects;

namespace Operations.Application.Features.Flights;

public sealed record SaveFlightDepartureCommand(Guid FlightId, SaveFlightDepartureRequest Departure, byte[] RowVersion)
    : ICommand<FlightDepartureSavedDto>;

public sealed class SaveFlightDepartureCommandValidator : AbstractValidator<SaveFlightDepartureCommand>
{
    public SaveFlightDepartureCommandValidator()
    {
        RuleFor(command => command.FlightId).NotEmpty();
        RuleFor(command => command.RowVersion).Must(version => version is { Length: 8 });
        RuleFor(command => command.Departure).NotNull();
        When(command => command.Departure is not null, () =>
        {
            RuleFor(command => command.Departure.Atd).NotEmpty();
            RuleFor(command => command.Departure.TargetSubmissionId).Must(id => id is null || id != Guid.Empty);
        });
    }
}

public sealed class SaveFlightDepartureCommandHandler(
    IOperationsDbContext db,
    IMobileFlightScope scope,
    MasterDataResolver resolver,
    TimeProvider clock,
    IMobileSyncBroadcaster mobileSync,
    IFileStorage storage,
    IUserDisplayNameReader? userNames = null) : ICommandHandler<SaveFlightDepartureCommand, FlightDepartureSavedDto>
{
    public const int MaxSignatureBytes = 2 * 1024 * 1024;

    public async Task<Result<FlightDepartureSavedDto>> Handle(SaveFlightDepartureCommand request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure)
            return resolved.Error;
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
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");
        if (flight.CancellationSubmissions.Any(c => c.SubmittedByEmployee.StaffMemberId == actor.StaffMemberId))
            return Error.Conflict("Revert your cancellation before submitting departure details.", "Operations.Flight.OwnCancellationSubmitted");

        var names = await FlightWorkAttribution.NamesAsync(userNames,
            FlightWorkAttribution.UserIds(flight).Append(actor.UserId), cancellationToken);
        var input = request.Departure;
        if (input.TargetSubmissionId == Guid.Empty)
            return Error.Validation("Target departure submission is required.", "Operations.DepartureSubmission.TargetRequired");
        var existing = input.TargetSubmissionId is { } targetId
            ? flight.DepartureSubmissions.SingleOrDefault(d => d.Id == targetId)
            : flight.DepartureSubmissions.SingleOrDefault(d => d.SubmittedByEmployee.StaffMemberId == actor.StaffMemberId);
        if (input.TargetSubmissionId.HasValue && existing is null)
            return Error.NotFound("Departure submission not found.", "Operations.DepartureSubmission.NotFound");
        if (existing is null && flight.DepartureSubmissions.Count == 1)
            existing = flight.DepartureSubmissions.Single();
        if (existing is null && flight.DepartureSubmissions.Count > 1)
            return Error.Conflict("Select the departure submission to update.", "Operations.DepartureSubmission.TargetRequired");
        var foreign = existing is not null && existing.SubmittedByEmployee.StaffMemberId != actor.StaffMemberId;
        if (foreign && !flight.CanCorrectOperationalWork(actor.StaffMemberId))
            return Error.Forbidden("Only assigned employees can update another employee's departure.", "Operations.DepartureSubmission.AssignmentRequired");
        if (input.Atd == default)
            return Error.Validation("Actual departure time is required.", "Operations.DepartureDetails.AtdRequired");
        if (input.SignatureContent is { } content)
        {
            if (content.Length > MaxSignatureBytes)
                return Error.Validation("The customer signature must be at most 2 MB.", "Operations.Departure.SignatureTooLarge");
            if (!IsPng(content))
                return Error.Validation("The customer signature must be a PNG image.", "Operations.Departure.SignatureInvalid");
            if (input.SignatureSignedAtUtc is null || input.SignatureSignedAtUtc == default(DateTimeOffset))
                return Error.Validation("Customer signing time is required.", "Operations.CustomerSignature.SignedAtRequired");
        }
        else if (input.SignatureSignedAtUtc is not null)
            return Error.Validation("Customer signature is required.", "Operations.DepartureDetails.CustomerSignatureRequired");

        // Replaying the same signed input after a lost response does not create a second file
        // or require its now-obsolete rowversion. New input must still pass concurrency below.
        if (existing is not null && existing.Details.Atd == input.Atd && existing.Details.Remarks == Normalize(input.Remarks) &&
            (foreign || existing.Details.HandledByEmployee.StaffMemberId == actor.StaffMemberId) &&
            (input.SignatureContent is null ||
                existing.Details.CustomerSignature is { } existingSignature && existingSignature.SignedAtUtc == input.SignatureSignedAtUtc &&
                await SignatureMatchesAsync(existingSignature.StorageKey, input.SignatureContent, cancellationToken)))
            return new FlightDepartureSavedDto(FlightDepartureSubmissionDto.Map(existing, names), Convert.ToBase64String(flight.RowVersion));

        if (!flight.RowVersion.SequenceEqual(request.RowVersion))
            return ConcurrencyErrors.Stale;
        if (flight.CancellationSubmissions.Any(c => c.SubmittedByEmployee.StaffMemberId != actor.StaffMemberId) &&
            !input.AcknowledgeConflictingSubmission)
            return Error.Conflict("Another employee reported this flight as canceled. Confirm before saving work details.",
                "Operations.Flight.CancellationConfirmationRequired");
        var arrivalRequired = FlightActualRules.RequireArrival(flight, existing?.SubmittedByEmployee.StaffMemberId ?? actor.StaffMemberId);
        if (arrivalRequired.IsFailure) return arrivalRequired.Error;
        var chronology = FlightActualRules.Chronology(flight, existing?.SubmittedByEmployee.StaffMemberId ?? actor.StaffMemberId,
            departureOverride: input.Atd);
        if (chronology.IsFailure) return chronology.Error;
        if (existing is null || existing.Details.Atd != input.Atd)
        {
            var actualsStaffId = existing?.SubmittedByEmployee.StaffMemberId ?? actor.StaffMemberId;
            var workBounds = FlightServiceBounds.ValidateExistingServices(flight, actualsStaffId, departureOverride: input.Atd);
            if (workBounds.IsFailure) return workBounds.Error;
            var taskBounds = FlightTaskBounds.ValidateExistingTasks(flight, actualsStaffId, departureOverride: input.Atd);
            if (taskBounds.IsFailure) return taskBounds.Error;
        }
        var completedWork = FlightServiceBounds.RequireCompletedForDeparture(flight);
        if (completedWork.IsFailure) return completedWork.Error;
        var completedTasks = FlightTaskBounds.RequireCompletedForDeparture(flight);
        if (completedTasks.IsFailure) return completedTasks.Error;
        var staff = await resolver.StaffMembersForStationAsync([actor.StaffMemberId], flight.Station.StationId, cancellationToken);
        if (staff.IsFailure)
            return staff.Error;
        var employee = staff.Value.Single();
        StoredFile? stored = null;
        var signature = existing?.Details.CustomerSignature;
        if (input.SignatureContent is not null)
        {
            using var stream = new MemoryStream(input.SignatureContent, writable: false);
            stored = await storage.SaveAsync("flight-departure-signatures", "customer-signature.png", "image/png", stream, cancellationToken);
            var created = CustomerSignature.Create("customer-signature.png", stored.StorageKey, stored.ContentType,
                stored.SizeBytes, input.SignatureSignedAtUtc!.Value);
            if (created.IsFailure)
            {
                await DeleteUnreferencedSignatureAsync(stored.StorageKey);
                return created.Error;
            }
            signature = created.Value;
        }
        var details = DepartureDetails.Create(input.Atd, signature, foreign ? existing!.Details.HandledByEmployee : employee, input.Remarks);
        if (details.IsFailure)
        {
            if (stored is not null) await DeleteUnreferencedSignatureAsync(stored.StorageKey);
            return details.Error;
        }
        var previousKey = existing?.Details.CustomerSignature?.StorageKey;
        var previousDetails = existing?.Details;
        var now = clock.GetUtcNow();
        db.SetOriginalRowVersion(flight, request.RowVersion);
        var submitted = flight.SubmitDeparture(employee, details.Value, actor.UserId, now, existing?.Id);
        if (submitted.IsFailure)
        {
            if (stored is not null) await DeleteUnreferencedSignatureAsync(stored.StorageKey);
            return submitted.Error;
        }
        if (foreign && previousDetails != submitted.Value.Details)
            db.Enqueue(new global::Operations.Contracts.FlightSubmissionUpdated
            {
                FlightId = flight.Id, FlightNumber = flight.FlightNumber.Value, SubmissionId = submitted.Value.Id,
                SubmissionKind = global::Operations.Contracts.FlightSubmissionKind.Departure,
                SubmittedByStaffMemberId = submitted.Value.SubmittedByEmployee.StaffMemberId,
                SubmittedByUserId = submitted.Value.CreatedByUserId,
                UpdatedByStaffMemberId = actor.StaffMemberId, UpdatedByUserId = actor.UserId,
                UpdatedByDisplayName = employee.FullName, OccurredOnUtc = now
            });
        try
        {
            await FlightActivityRecorder.TouchAsync(db, flight, actor, clock.GetUtcNow(), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (stored is not null) await DeleteUnreferencedSignatureAsync(stored.StorageKey);
            return ConcurrencyErrors.Stale;
        }
        catch (DbUpdateException) when (existing is null)
        {
            if (stored is not null) await DeleteUnreferencedSignatureAsync(stored.StorageKey);
            if (await db.Flights.AsNoTracking().AnyAsync(f => f.Id == request.FlightId && f.DepartureSubmissions.Any(), cancellationToken))
                return ConcurrencyErrors.Stale;
            throw;
        }
        catch
        {
            // Preserve the save failure. An ambiguous commit must not remove signature bytes
            // already referenced by a departure, and request cancellation must not cancel cleanup.
            if (stored is not null) await DeleteUnreferencedSignatureAsync(stored.StorageKey);
            throw;
        }
        mobileSync.Enqueue(new MobileSyncChange(MobileSyncTables.Flights, MobileSyncOps.Refresh, null,
            MobileSyncAudience.AllStations, clock.GetUtcNow()));
        if (stored is not null && previousKey is not null && previousKey != stored.StorageKey)
            await DeleteUnreferencedSignatureAsync(previousKey);
        return new FlightDepartureSavedDto(FlightDepartureSubmissionDto.Map(submitted.Value, names), Convert.ToBase64String(flight.RowVersion));
    }

    private async Task DeleteUnreferencedSignatureAsync(string key)
    {
        try
        {
            var referenced = await db.Flights.AsNoTracking().AnyAsync(f =>
                f.DepartureSubmissions.Any(d => d.Details.CustomerSignature != null && d.Details.CustomerSignature.StorageKey == key) ||
                f.ReturnToRamps.Any(r => r.DepartureDetails != null && r.DepartureDetails.CustomerSignature != null &&
                    r.DepartureDetails.CustomerSignature.StorageKey == key), CancellationToken.None);
            if (!referenced) await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch
        {
            // Storage/connection cleanup is best effort. Do not obscure the original failure
            // or turn a successfully committed departure into an apparent failed save.
        }
    }

    private async Task<bool> SignatureMatchesAsync(string key, byte[] content, CancellationToken cancellationToken)
    {
        await using var previous = await storage.OpenAsync(key, cancellationToken);
        if (previous is null) return false;
        using var buffer = new MemoryStream();
        await previous.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray().AsSpan().SequenceEqual(content);
    }

    // Validate the PNG envelope and dimensions without introducing a platform image decoder.
    internal static bool IsPng(byte[] content)
    {
        if (content.Length < 45 || !content.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            BinaryPrimitives.ReadUInt32BigEndian(content.AsSpan(8, 4)) != 13 ||
            !content.AsSpan(12, 4).SequenceEqual("IHDR"u8) ||
            BinaryPrimitives.ReadUInt32BigEndian(content.AsSpan(16, 4)) == 0 ||
            BinaryPrimitives.ReadUInt32BigEndian(content.AsSpan(20, 4)) == 0)
            return false;
        var offset = 8;
        var hasImageData = false;
        while (offset <= content.Length - 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(content.AsSpan(offset, 4));
            if (length > content.Length - offset - 12) return false;
            var type = content.AsSpan(offset + 4, 4);
            if (type.SequenceEqual("IDAT"u8)) hasImageData |= length > 0;
            if (type.SequenceEqual("IEND"u8)) return length == 0 && hasImageData && offset + 12 == content.Length;
            offset += (int)length + 12;
        }
        return false;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
