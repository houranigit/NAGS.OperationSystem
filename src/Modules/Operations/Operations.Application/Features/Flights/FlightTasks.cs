using BuildingBlocks.Application.Abstractions;
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
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;

namespace Operations.Application.Features.Flights;

public sealed record SaveFlightTaskCommand(Guid FlightId, Guid RecordId, SaveFlightTaskRequest Task, byte[] RowVersion)
    : ICommand<FlightTaskSavedDto>;
public sealed record DeleteFlightTaskCommand(Guid FlightId, Guid RecordId, byte[] RowVersion) : ICommand<FlightTaskRemovedDto>;

public sealed class SaveFlightTaskCommandValidator : AbstractValidator<SaveFlightTaskCommand>
{
    public SaveFlightTaskCommandValidator()
    {
        RuleFor(c => c.FlightId).NotEmpty(); RuleFor(c => c.RecordId).NotEmpty();
        RuleFor(c => c.RowVersion).Must(v => v is { Length: 8 }); RuleFor(c => c.Task).NotNull();
    }
}

public sealed class DeleteFlightTaskCommandValidator : AbstractValidator<DeleteFlightTaskCommand>
{
    public DeleteFlightTaskCommandValidator()
    {
        RuleFor(c => c.FlightId).NotEmpty(); RuleFor(c => c.RecordId).NotEmpty();
        RuleFor(c => c.RowVersion).Must(v => v is { Length: 8 });
    }
}

public sealed class SaveFlightTaskCommandHandler(IOperationsDbContext db, IMobileFlightScope scope, MasterDataResolver resolver,
    IMasterDataReader masterData, TimeProvider clock, IMobileSyncBroadcaster mobileSync, IFileStorage storage)
    : ICommandHandler<SaveFlightTaskCommand, FlightTaskSavedDto>
{
    public async Task<Result<FlightTaskSavedDto>> Handle(SaveFlightTaskCommand request, CancellationToken ct)
    {
        var resolved = await scope.ResolveAsync(ct);
        if (resolved.IsFailure) return resolved.Error;
        var actor = resolved.Value;
        var flight = await FlightServiceWrites.Load(db, actor).SingleOrDefaultAsync(f => f.Id == request.FlightId, ct);
        if (flight is null) return FlightTaskWrites.NotFound();
        if (flight.CancellationSubmissions.Count > 0) return FlightTaskWrites.Cancellation();
        if (request.RecordId == Guid.Empty || request.RowVersion is not { Length: 8 } || request.Task is null)
            return Error.Validation("A task record and current flight version are required.", "Operations.FlightTask.RequestInvalid");
        var input = request.Task with { Attachments = request.Task.Attachments?.Select(item => item is null ? null! : item with
            { ContentType = NormalizeMime(item.ContentType) }).ToList() };
        var existing = flight.TaskRecords.SingleOrDefault(t => t.Id == request.RecordId);
        if (existing?.ReturnToRampId is not null) return FlightTaskWrites.NotFound();
        var validation = FlightTaskInputs.Validate(input, existing);
        if (validation.IsFailure) return validation.Error;
        foreach (var attachment in input.Attachments ?? [])
        {
            var retained = existing?.Attachments.SingleOrDefault(a => a.Id == attachment.Id);
            if (retained is not null && attachment.Content is not null && !await FlightTaskFiles.ContentMatchesAsync(storage, retained, attachment.Content, ct))
                return Error.Validation("Use a new attachment ID to replace a file.", "Operations.FlightTask.AttachmentImmutable");
        }
        var coreMatches = existing is not null && FlightTaskInputs.CoreMatches(existing, input);
        var mediaMatches = existing is not null && await FlightTaskFiles.MatchesAsync(storage, existing, input.Attachments, ct);
        if (existing is not null && coreMatches && existing.Window.ToUtc == input.EndUtc && mediaMatches)
            return FlightTaskWrites.Saved(flight, existing, actor.UserId);
        var bounds = FlightTaskBounds.Validate(flight, input, actor.StaffMemberId);
        if (bounds.IsFailure) return bounds.Error;
        var completed = FlightTaskBounds.RequireCompletedIfDeparted(flight, input, actor.StaffMemberId);
        if (completed.IsFailure) return completed.Error;
        var closed = FlightTaskBounds.RequireClosedPeriods(input);
        if (closed.IsFailure) return closed.Error;
        if (existing is not null)
        {
            if ((!coreMatches || !mediaMatches) && existing.CreatedByUserId != actor.UserId)
                return Error.Forbidden("Only the employee who created this task can change its details.", "Operations.FlightTask.ReadOnly");
            if (existing.Window.ToUtc != input.EndUtc && existing.Window.ToUtc is not null && existing.EndedByUserId != actor.UserId)
                return Error.Forbidden("Only the employee who entered the end time can change or clear it.", "Operations.FlightTask.EndReadOnly");
        }
        if (!flight.RowVersion.SequenceEqual(request.RowVersion)) return ConcurrencyErrors.Stale;
        var performerIds = input.Performers.Select(item => item.Id).ToArray();
        var toolIds = input.Tools.Select(item => item.Id).ToArray();
        var materialIds = input.Materials.Select(item => item.Id).ToArray();
        var supportIds = input.GeneralSupports.Select(item => item.Id).ToArray();
        var attachmentIds = input.Attachments?.Select(item => item.Id).ToArray() ?? [];
        var foreignIds = await db.Flights.AsNoTracking().AnyAsync(f =>
            f.Id != request.FlightId && f.TaskRecords.Any(t => t.Id == request.RecordId) ||
            f.TaskRecords.Any(t => (f.Id != request.FlightId || t.Id != request.RecordId) &&
                (t.Performers.Any(p => performerIds.Contains(p.Id)) || t.Tools.Any(r => toolIds.Contains(r.Id)) ||
                 t.Materials.Any(r => materialIds.Contains(r.Id)) || t.GeneralSupports.Any(r => supportIds.Contains(r.Id)) ||
                 t.Attachments.Any(a => attachmentIds.Contains(a.Id)))), ct);
        if (foreignIds) return Error.Conflict("A task or child record ID is already in use.", "Operations.FlightTask.IdConflict");
        var details = await FlightTaskInputs.ResolveAsync(input, existing, flight.Station.StationId, masterData, resolver, ct);
        if (details.IsFailure) return details.Error;
        var value = details.Value;
        var saved = flight.SaveTask(request.RecordId, (FlightTaskType)input.TaskType, value.AtaChapter, input.CustomerReferenceNumber,
            value.Window, input.Notes, value.Performers, value.Tools, value.Materials, value.GeneralSupports, actor.UserId, clock.GetUtcNow());
        if (saved.IsFailure) return saved.Error;
        var record = saved.Value;
        var createdKeys = new List<string>();
        var removedKeys = new List<string>();
        try
        {
            if (input.Attachments is not null)
            {
                foreach (var removed in record.Attachments.Where(a => input.Attachments.All(item => item.Id != a.Id)).ToArray())
                {
                    removedKeys.Add(removed.Details.StorageKey);
                    var removal = flight.RemoveTaskAttachment(record.Id, removed.Id, actor.UserId, clock.GetUtcNow());
                    if (removal.IsFailure) return removal.Error;
                }
                foreach (var attachment in input.Attachments)
                {
                    if (record.Attachments.Any(a => a.Id == attachment.Id)) continue;
                    using var content = new MemoryStream(attachment.Content!, writable: false);
                    var stored = await storage.SaveAsync("flight-task-attachments", NormalizeName(attachment.FileName), attachment.ContentType.Trim(), content, ct);
                    createdKeys.Add(stored.StorageKey);
                    var fileDetails = AttachmentDetails.Create(attachment.FileName, stored.StorageKey, stored.ContentType, stored.SizeBytes, (AttachmentKind)attachment.Kind);
                    if (fileDetails.IsFailure) { await FlightTaskFiles.DeleteUnreferencedAsync(db, storage, createdKeys); return fileDetails.Error; }
                    var added = flight.AddTaskAttachment(record.Id, fileDetails.Value, actor.UserId, clock.GetUtcNow(), id: attachment.Id);
                    if (added.IsFailure) { await FlightTaskFiles.DeleteUnreferencedAsync(db, storage, createdKeys); return added.Error; }
                }
            }
            db.SetOriginalRowVersion(flight, request.RowVersion);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await FlightTaskFiles.DeleteUnreferencedAsync(db, storage, createdKeys.Concat(removedKeys));
            return ConcurrencyErrors.Stale;
        }
        catch (DbUpdateException) when (existing is null)
        {
            await FlightTaskFiles.DeleteUnreferencedAsync(db, storage, createdKeys.Concat(removedKeys));
            if (await db.Flights.AsNoTracking().AnyAsync(f => f.TaskRecords.Any(t => t.Id == request.RecordId), ct)) return ConcurrencyErrors.Stale;
            throw;
        }
        catch
        {
            await FlightTaskFiles.DeleteUnreferencedAsync(db, storage, createdKeys.Concat(removedKeys));
            throw;
        }
        await FlightTaskFiles.DeleteUnreferencedAsync(db, storage, removedKeys);
        FlightServiceWrites.Notify(mobileSync, clock);
        return FlightTaskWrites.Saved(flight, record, actor.UserId);
    }

    private static string NormalizeMime(string? value)
    {
        var mime = (value ?? "").Trim().ToLowerInvariant();
        return mime == "audio/x-m4a" ? "audio/mp4" : mime;
    }
    private static string NormalizeName(string value)
    {
        var name = Path.GetFileName(value.Trim().Replace('\\', '/'));
        return name.Length <= 255 ? name : name[..255];
    }
}

public sealed class DeleteFlightTaskCommandHandler(IOperationsDbContext db, IMobileFlightScope scope, TimeProvider clock,
    IMobileSyncBroadcaster mobileSync, IFileStorage storage) : ICommandHandler<DeleteFlightTaskCommand, FlightTaskRemovedDto>
{
    public async Task<Result<FlightTaskRemovedDto>> Handle(DeleteFlightTaskCommand request, CancellationToken ct)
    {
        var resolved = await scope.ResolveAsync(ct);
        if (resolved.IsFailure) return resolved.Error;
        var flight = await FlightServiceWrites.Load(db, resolved.Value).SingleOrDefaultAsync(f => f.Id == request.FlightId, ct);
        if (flight is null) return FlightTaskWrites.NotFound();
        if (flight.CancellationSubmissions.Count > 0) return FlightTaskWrites.Cancellation();
        if (request.RecordId == Guid.Empty || request.RowVersion is not { Length: 8 })
            return Error.Validation("A task record and current flight version are required.", "Operations.FlightTask.RequestInvalid");
        var record = flight.TaskRecords.SingleOrDefault(t => t.Id == request.RecordId);
        if (record?.ReturnToRampId is not null) return FlightTaskWrites.NotFound();
        if (record is not null)
        {
            if (record.CreatedByUserId != resolved.Value.UserId || record.Window.ToUtc is not null && record.EndedByUserId != resolved.Value.UserId)
                return Error.Forbidden("Only its creator can delete a task, and another employee's end time must be retained.", "Operations.FlightTask.DeleteReadOnly");
            if (!flight.RowVersion.SequenceEqual(request.RowVersion)) return ConcurrencyErrors.Stale;
            var keys = record.Attachments.Select(a => a.Details.StorageKey).ToArray();
            var removed = flight.RemoveTask(record.Id, resolved.Value.UserId, clock.GetUtcNow());
            if (removed.IsFailure) return removed.Error;
            db.SetOriginalRowVersion(flight, request.RowVersion);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return ConcurrencyErrors.Stale; }
            catch { await FlightTaskFiles.DeleteUnreferencedAsync(db, storage, keys); throw; }
            await FlightTaskFiles.DeleteUnreferencedAsync(db, storage, keys);
            FlightServiceWrites.Notify(mobileSync, clock);
        }
        var counts = FlightServiceWrites.Counts(flight);
        return new FlightTaskRemovedDto(request.RecordId, Convert.ToBase64String(flight.RowVersion), counts.Active, counts.Completed);
    }
}

internal static class FlightTaskWrites
{
    internal static FlightTaskSavedDto Saved(Flight flight, FlightTask task, Guid actor)
    {
        var counts = FlightServiceWrites.Counts(flight);
        return new(FlightTaskDto.Map(task, actor, true), Convert.ToBase64String(flight.RowVersion), counts.Active, counts.Completed);
    }
    internal static Error NotFound() => Error.NotFound("Flight task not found.", "Operations.FlightTask.NotFound");
    internal static Error Cancellation() => Error.Conflict("A flight with cancellation submissions cannot have tasks.", "Operations.Flight.CancellationSubmitted");
}
