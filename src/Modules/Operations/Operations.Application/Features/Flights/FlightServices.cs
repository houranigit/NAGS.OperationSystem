using BuildingBlocks.Application.Abstractions;
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
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;

namespace Operations.Application.Features.Flights;

public sealed record SaveFlightServiceCommand(Guid FlightId, Guid RecordId, SaveFlightServiceRequest Service, byte[] RowVersion)
    : ICommand<FlightServiceSavedDto>;
public sealed record DeleteFlightServiceCommand(Guid FlightId, Guid RecordId, byte[] RowVersion) : ICommand<FlightServiceRemovedDto>;

public sealed class SaveFlightServiceCommandValidator : AbstractValidator<SaveFlightServiceCommand>
{
    public SaveFlightServiceCommandValidator()
    {
        RuleFor(c => c.FlightId).NotEmpty();
        RuleFor(c => c.RecordId).NotEmpty();
        RuleFor(c => c.RowVersion).Must(v => v is { Length: 8 });
        RuleFor(c => c.Service).NotNull();
    }
}

public sealed class DeleteFlightServiceCommandValidator : AbstractValidator<DeleteFlightServiceCommand>
{
    public DeleteFlightServiceCommandValidator()
    {
        RuleFor(c => c.FlightId).NotEmpty();
        RuleFor(c => c.RecordId).NotEmpty();
        RuleFor(c => c.RowVersion).Must(v => v is { Length: 8 });
    }
}

public sealed class SaveFlightServiceCommandHandler(IOperationsDbContext db, IMobileFlightScope scope, MasterDataResolver resolver,
    TimeProvider clock, IMobileSyncBroadcaster mobileSync, IFileStorage storage) : ICommandHandler<SaveFlightServiceCommand, FlightServiceSavedDto>
{
    public async Task<Result<FlightServiceSavedDto>> Handle(SaveFlightServiceCommand request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure) return resolved.Error;
        var actor = resolved.Value;
        var flight = await FlightServiceWrites.Load(db, actor).SingleOrDefaultAsync(f => f.Id == request.FlightId, cancellationToken);
        if (flight is null) return FlightServiceWrites.NotFound();
        if (flight.CancellationSubmissions.Count > 0) return FlightServiceWrites.Cancellation();
        if (request.RecordId == Guid.Empty || request.RowVersion is not { Length: 8 } || request.Service is null)
            return Error.Validation("A service record and current flight version are required.", "Operations.FlightService.RequestInvalid");
        var input = request.Service;
        var existing = flight.ServiceRecords.SingleOrDefault(s => s.Id == request.RecordId);
        if (existing?.ReturnToRampId is not null) return FlightServiceWrites.NotFound();
        var eligibility = await resolver.EnsurePerformedServiceAllowedAsync(input.ServiceId, actor.StaffMemberId, flight.Station.StationId, cancellationToken);
        if (eligibility.IsFailure) return eligibility.Error;
        var validation = ValidateInput(input, existing);
        if (validation.IsFailure) return validation.Error;
        foreach (var attachment in input.Attachments ?? [])
        {
            var retained = existing?.Attachments.SingleOrDefault(a => a.Id == attachment.Id);
            if (retained is not null && attachment.Content is not null &&
                !await FlightServiceFiles.ContentMatchesAsync(storage, retained, attachment.Content, cancellationToken))
                return Error.Validation("Use a new attachment ID to replace a file.", "Operations.FlightService.AttachmentImmutable");
        }
        var mediaMatches = existing is not null && await FlightServiceFiles.MatchesAsync(storage, existing, input.Attachments, cancellationToken);
        var coreMatches = existing is not null && CoreMatches(existing, input);
        if (existing is not null && coreMatches && existing.Window.ToUtc == input.EndUtc && mediaMatches)
            return Saved(flight, existing, actor.UserId);

        var bounds = FlightServiceBounds.Validate(flight, input, actor.StaffMemberId);
        if (bounds.IsFailure) return bounds.Error;
        var completed = FlightServiceBounds.RequireCompletedIfDeparted(flight, input, actor.StaffMemberId);
        if (completed.IsFailure) return completed.Error;
        if (existing is not null)
        {
            if ((!coreMatches || !mediaMatches) && existing.CreatedByUserId != actor.UserId)
                return Error.Forbidden("Only the employee who created this service can change its details.", "Operations.FlightService.ReadOnly");
            if (existing.Window.ToUtc != input.EndUtc && existing.Window.ToUtc is not null && existing.EndedByUserId != actor.UserId)
                return Error.Forbidden("Only the employee who entered the end time can change or clear it.", "Operations.FlightService.EndReadOnly");
        }
        if (!flight.RowVersion.SequenceEqual(request.RowVersion)) return ConcurrencyErrors.Stale;
        var performerIds = input.Performers.Select(p => p.Id).ToArray();
        var attachmentIds = input.Attachments?.Select(a => a.Id).ToArray() ?? [];
        var foreignIds = await db.Flights.AsNoTracking().AnyAsync(f =>
            f.Id != request.FlightId && f.ServiceRecords.Any(s => s.Id == request.RecordId) ||
            f.ServiceRecords.Any(s => (f.Id != request.FlightId || s.Id != request.RecordId) &&
                (s.Performers.Any(p => performerIds.Contains(p.Id)) || s.Attachments.Any(a => attachmentIds.Contains(a.Id)))), cancellationToken);
        if (foreignIds)
            return Error.Conflict("A service or child record ID is already in use.", "Operations.FlightService.IdConflict");
        var services = await resolver.ServicesAsync([input.ServiceId], cancellationToken);
        if (services.IsFailure) return services.Error;
        // Retained stable performer selections preserve their historical snapshots after a move
        // or deactivation. New/replaced selections must still be active at the flight's station.
        var newStaffIds = input.Performers.Where(p => existing?.Performers.Any(old =>
                old.Id == p.Id && old.Employee.StaffMemberId == p.StaffMemberId) != true)
            .Select(p => p.StaffMemberId).ToArray();
        var staff = await resolver.StaffMembersForStationAsync(newStaffIds, flight.Station.StationId, cancellationToken);
        if (staff.IsFailure) return staff.Error;
        var window = TimeWindow.Create(input.StartUtc, input.EndUtc);
        if (window.IsFailure) return window.Error;
        var performers = new List<FlightServicePerformerInput>();
        foreach (var performer in input.Performers)
        {
            var period = TimeWindow.Create(performer.StartUtc, performer.EndUtc);
            if (period.IsFailure) return period.Error;
            var retained = existing?.Performers.SingleOrDefault(p => p.Id == performer.Id && p.Employee.StaffMemberId == performer.StaffMemberId);
            var employee = retained?.Employee ?? staff.Value.Single(p => p.StaffMemberId == performer.StaffMemberId);
            performers.Add(new(employee, period.Value, performer.Id));
        }
        var serviceSnapshot = existing is not null && existing.CreatedByUserId != actor.UserId ? existing.Service : services.Value.Single();
        var saved = flight.SaveService(request.RecordId, serviceSnapshot, input.CustomerReferenceNumber, window.Value, input.Notes,
            performers, actor.UserId, clock.GetUtcNow());
        if (saved.IsFailure) return saved.Error;
        var record = saved.Value;
        var createdKeys = new List<string>();
        var removedKeys = new List<string>();
        try
        {
            if (input.Attachments is not null)
            {
                // Attachment IDs name immutable bytes. Retaining them omits content; replacing a file uses a new ID.
                foreach (var removed in record.Attachments.Where(a => input.Attachments.All(i => i.Id != a.Id)).ToArray())
                {
                    removedKeys.Add(removed.Details.StorageKey);
                    var removal = flight.RemoveServiceAttachment(record.Id, removed.Id, actor.UserId, clock.GetUtcNow());
                    if (removal.IsFailure) return removal.Error;
                }
                foreach (var attachment in input.Attachments)
                {
                    var retained = record.Attachments.SingleOrDefault(a => a.Id == attachment.Id);
                    if (retained is not null) continue;
                    using var content = new MemoryStream(attachment.Content!, writable: false);
                    var stored = await storage.SaveAsync("flight-service-attachments", NormalizeFileName(attachment.FileName),
                        attachment.ContentType.Trim(), content, cancellationToken);
                    createdKeys.Add(stored.StorageKey);
                    var details = AttachmentDetails.Create(attachment.FileName, stored.StorageKey, stored.ContentType, stored.SizeBytes, (AttachmentKind)attachment.Kind);
                    if (details.IsFailure) { await FlightServiceFiles.DeleteUnreferencedAsync(db, storage, createdKeys); return details.Error; }
                    var added = flight.AddServiceAttachment(record.Id, details.Value, actor.UserId, clock.GetUtcNow(), id: attachment.Id);
                    if (added.IsFailure) { await FlightServiceFiles.DeleteUnreferencedAsync(db, storage, createdKeys); return added.Error; }
                }
            }
            db.SetOriginalRowVersion(flight, request.RowVersion);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await FlightServiceFiles.DeleteUnreferencedAsync(db, storage, createdKeys.Concat(removedKeys));
            return ConcurrencyErrors.Stale;
        }
        catch (DbUpdateException) when (existing is null)
        {
            await FlightServiceFiles.DeleteUnreferencedAsync(db, storage, createdKeys.Concat(removedKeys));
            if (await db.Flights.AsNoTracking().AnyAsync(f => f.ServiceRecords.Any(s => s.Id == request.RecordId), cancellationToken))
                return ConcurrencyErrors.Stale;
            throw;
        }
        catch
        {
            await FlightServiceFiles.DeleteUnreferencedAsync(db, storage, createdKeys.Concat(removedKeys));
            throw;
        }
        await FlightServiceFiles.DeleteUnreferencedAsync(db, storage, removedKeys);
        FlightServiceWrites.Notify(mobileSync, clock);
        return Saved(flight, record, actor.UserId);
    }

    private static Result ValidateInput(SaveFlightServiceRequest input, FlightService? existing)
    {
        if (input.ServiceId == Guid.Empty || input.StartUtc == default)
            return Error.Validation("Service and start time are required.", "Operations.FlightService.DetailsRequired");
        if (input.Performers is not { Count: > 0 })
            return Error.Validation("Every service requires at least one performer.", "Operations.WorkOrder.ServiceLinePerformerRequired");
        if (input.Performers.Any(p => p is null || p.Id == Guid.Empty || p.StaffMemberId == Guid.Empty || p.StartUtc == default) ||
            input.Performers.Select(p => p.Id).Distinct().Count() != input.Performers.Count ||
            input.Performers.Select(p => p.StaffMemberId).Distinct().Count() != input.Performers.Count)
            return Error.Validation("Provide exactly one identified working period for every selected employee.", "Operations.WorkOrder.EmployeeAssignmentsRequired");
        if (input.Attachments is null) return Result.Success();
        if (input.Attachments.Count > FlightService.MaxAttachments)
            return Error.Validation("A service can have at most 10 attachments.", "Operations.WorkOrder.AttachmentLimitExceeded");
        if (input.Attachments.Any(a => a is null || a.Id == Guid.Empty) || input.Attachments.Select(a => a.Id).Distinct().Count() != input.Attachments.Count)
            return Error.Validation("Attachment IDs must be present and unique.", "Operations.FlightService.AttachmentIdInvalid");
        long aggregateBytes = 0;
        foreach (var attachment in input.Attachments)
        {
            var retained = existing?.Attachments.SingleOrDefault(a => a.Id == attachment.Id);
            var details = AttachmentDetails.Create(attachment.FileName, "validation", attachment.ContentType,
                attachment.Content?.LongLength ?? retained?.Details.SizeBytes ?? 0, (AttachmentKind)attachment.Kind);
            if (details.IsFailure) return details.Error;
            if (retained is not null && (details.Value.FileName != retained.Details.FileName || !details.Value.ContentType.Equals(retained.Details.ContentType, StringComparison.OrdinalIgnoreCase) || details.Value.Kind != retained.Details.Kind))
                return Error.Validation("Use a new attachment ID to replace a file.", "Operations.FlightService.AttachmentImmutable");
            if (attachment.Content is null)
            {
                if (retained is null) return Error.Validation("A new attachment requires file content.", "Operations.FlightService.AttachmentContentRequired");
            }
            else
            {
                var file = FlightServiceAttachmentPolicy.Validate((AttachmentKind)attachment.Kind, attachment.Content, attachment.FileName, attachment.ContentType);
                if (file.IsFailure) return file.Error;
                aggregateBytes += attachment.Content.LongLength;
                if (aggregateBytes > FlightServiceAttachmentPolicy.MaxAggregateBytes)
                    return Error.Validation("The combined inline attachments exceed the 25 MB request limit.", "Operations.FlightService.InlineFilesTooLarge");
            }
        }
        return Result.Success();
    }

    private static bool CoreMatches(FlightService service, SaveFlightServiceRequest input) =>
        service.Service.ServiceId == input.ServiceId && service.Window.FromUtc == input.StartUtc &&
        service.CustomerReferenceNumber == Normalize(input.CustomerReferenceNumber) && service.Notes == Normalize(input.Notes) &&
        service.Performers.Count == input.Performers.Count && service.Performers.All(p => input.Performers.Any(i =>
            i.Id == p.Id && i.StaffMemberId == p.Employee.StaffMemberId && i.StartUtc == p.Window.FromUtc && i.EndUtc == p.Window.ToUtc));

    private static FlightServiceSavedDto Saved(Flight flight, FlightService service, Guid actor)
    {
        var counts = FlightServiceWrites.Counts(flight);
        return new(FlightServiceDto.Map(service, actor, true), Convert.ToBase64String(flight.RowVersion), counts.Active, counts.Completed);
    }
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string NormalizeFileName(string value)
    {
        var name = Path.GetFileName(value.Trim().Replace('\\', '/'));
        return name.Length <= 255 ? name : name[..255];
    }
}

public sealed class DeleteFlightServiceCommandHandler(IOperationsDbContext db, IMobileFlightScope scope, TimeProvider clock,
    IMobileSyncBroadcaster mobileSync, IFileStorage storage, MasterDataResolver resolver) : ICommandHandler<DeleteFlightServiceCommand, FlightServiceRemovedDto>
{
    public async Task<Result<FlightServiceRemovedDto>> Handle(DeleteFlightServiceCommand request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure) return resolved.Error;
        var flight = await FlightServiceWrites.Load(db, resolved.Value).SingleOrDefaultAsync(f => f.Id == request.FlightId, cancellationToken);
        if (flight is null) return FlightServiceWrites.NotFound();
        if (flight.CancellationSubmissions.Count > 0) return FlightServiceWrites.Cancellation();
        if (request.RecordId == Guid.Empty || request.RowVersion is not { Length: 8 })
            return Error.Validation("A service record and current flight version are required.", "Operations.FlightService.RequestInvalid");
        var record = flight.ServiceRecords.SingleOrDefault(s => s.Id == request.RecordId);
        if (record?.ReturnToRampId is not null) return FlightServiceWrites.NotFound();
        if (record is not null)
        {
            var eligibility = await resolver.EnsurePerformedServiceAllowedAsync(record.Service.ServiceId, resolved.Value.StaffMemberId, flight.Station.StationId, cancellationToken);
            if (eligibility.IsFailure) return eligibility.Error;
            if (record.CreatedByUserId != resolved.Value.UserId || record.Window.ToUtc is not null && record.EndedByUserId != resolved.Value.UserId)
                return Error.Forbidden("Only its creator can delete a service, and another employee's end time must be retained.", "Operations.FlightService.DeleteReadOnly");
            if (!flight.RowVersion.SequenceEqual(request.RowVersion)) return ConcurrencyErrors.Stale;
            var keys = record.Attachments.Select(a => a.Details.StorageKey).ToArray();
            var removed = flight.RemoveService(record.Id, resolved.Value.UserId, clock.GetUtcNow());
            if (removed.IsFailure) return removed.Error;
            db.SetOriginalRowVersion(flight, request.RowVersion);
            try { await db.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return ConcurrencyErrors.Stale; }
            catch
            {
                // A response loss after a committed deletion must not leak its now-unreferenced files.
                await FlightServiceFiles.DeleteUnreferencedAsync(db, storage, keys);
                throw;
            }
            await FlightServiceFiles.DeleteUnreferencedAsync(db, storage, keys);
            FlightServiceWrites.Notify(mobileSync, clock);
        }
        var counts = FlightServiceWrites.Counts(flight);
        return new FlightServiceRemovedDto(request.RecordId, Convert.ToBase64String(flight.RowVersion), counts.Active, counts.Completed);
    }
}

internal static class FlightServiceWrites
{
    internal static IQueryable<Flight> Load(IOperationsDbContext db, MobileFlightScopeContext actor) =>
        MobileFlightQuery.ApplyScope(db.Flights, actor).AsSplitQuery()
            .Include(f => f.PlannedServices).Include(f => f.AssignedEmployees)
            .Include(f => f.ArrivalSubmissions).Include(f => f.DepartureSubmissions).Include(f => f.CancellationSubmissions)
            .Include(f => f.ReturnToRamps)
            .Include(f => f.ServiceRecords).ThenInclude(s => s.Performers)
            .Include(f => f.ServiceRecords).ThenInclude(s => s.Attachments)
            .Include(f => f.TaskRecords).ThenInclude(t => t.Tools)
            .Include(f => f.TaskRecords).ThenInclude(t => t.Materials)
            .Include(f => f.TaskRecords).ThenInclude(t => t.GeneralSupports)
            .Include(f => f.TaskRecords).ThenInclude(t => t.Performers)
            .Include(f => f.TaskRecords).ThenInclude(t => t.Attachments);

    internal static (int Active, int Completed) Counts(Flight flight)
    {
        var occurrenceId = flight.ReturnToRamps.OrderByDescending(r => r.Sequence).FirstOrDefault()?.Id;
        return (flight.ServiceRecords.Count(s => s.ReturnToRampId == occurrenceId && s.Window.ToUtc is null) +
                flight.TaskRecords.Count(t => t.ReturnToRampId == occurrenceId && t.Window.ToUtc is null),
            flight.ServiceRecords.Count(s => s.ReturnToRampId == occurrenceId && s.Window.ToUtc is not null) +
                flight.TaskRecords.Count(t => t.ReturnToRampId == occurrenceId && t.Window.ToUtc is not null));
    }
    internal static void Notify(IMobileSyncBroadcaster sync, TimeProvider clock) => sync.Enqueue(new MobileSyncChange(
        MobileSyncTables.Flights, MobileSyncOps.Refresh, null, MobileSyncAudience.AllStations, clock.GetUtcNow()));
    internal static Error NotFound() => Error.NotFound("Flight service not found.", "Operations.FlightService.NotFound");
    internal static Error Cancellation() => Error.Conflict("A flight with cancellation submissions cannot have performed services.", "Operations.Flight.CancellationSubmitted");
}
