using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Mobile;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using Identity.Contracts;
using MasterData.Contracts.Readers;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Contracts;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;

namespace Operations.Application.Features.Flights;

public sealed record SaveFlightReturnToRampCommand(Guid FlightId, Guid OccurrenceId, SaveFlightReturnToRampRequest Input, byte[] RowVersion,
    bool PortalOnly = false) : ICommand<FlightDetailDto>;

public sealed class SaveFlightReturnToRampCommandHandler(IOperationsDbContext db, IMobileFlightScope scope,
    MasterDataResolver resolver, IMasterDataReader masterData, IFileStorage storage, TimeProvider clock,
    IMobileSyncBroadcaster mobileSync, IUserDisplayNameReader? userNames = null, PortalFlightScope? portalScope = null, IUserContext? user = null)
    : ICommandHandler<SaveFlightReturnToRampCommand, FlightDetailDto>
{
    public async Task<Result<FlightDetailDto>> Handle(SaveFlightReturnToRampCommand request, CancellationToken ct)
    {
        var input = request.Input;
        if (request.FlightId == Guid.Empty || request.OccurrenceId == Guid.Empty || request.RowVersion is not { Length: 8 } ||
            input?.Arrival is null || input.Arrival.ArrivedAtUtc == default || input.Services is null || input.Tasks is null ||
            input.Services.Count + input.Tasks.Count > 100 || input.Services.Any(s => s is null || s.RecordId == Guid.Empty || s.Input is null) ||
            input.Tasks.Any(t => t is null || t.RecordId == Guid.Empty || t.Input is null) ||
            input.Services.Select(s => s.RecordId).Distinct().Count() != input.Services.Count || input.Tasks.Select(t => t.RecordId).Distinct().Count() != input.Tasks.Count)
            return Invalid("An identified RTR, arrival, current version and complete work lists are required.");
        var resolved = await FlightOperationAccess.ResolveCorrectionAsync(scope, portalScope, request.PortalOnly, ct);
        if (resolved.IsFailure) return resolved.Error;
        var access = resolved.Value;
        var isAdministrator = access.IsAdministratorView;
        var userId = access.UserId;
        var flight = await FlightServiceWrites.Load(access.Apply(db.Flights)).SingleOrDefaultAsync(f => f.Id == request.FlightId, ct);
        if (flight is null) return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");
        var existing = flight.ReturnToRamps.SingleOrDefault(r => r.Id == request.OccurrenceId);
        var priorServices = flight.ServiceRecords.Where(s => s.ReturnToRampId == request.OccurrenceId).ToArray();
        var priorTasks = flight.TaskRecords.Where(t => t.ReturnToRampId == request.OccurrenceId).ToArray();
        if (flight.CancellationSubmissions.Count > 0) return FlightServiceWrites.Cancellation();
        if (flight.DepartureSubmissions.Count == 0)
            return Error.Conflict("Record flight departure before adding RTR.", "Operations.ReturnToRamp.DepartureRequired");

        var station = await masterData.GetStationAsync(flight.Station.StationId, ct);
        if (station is null || string.IsNullOrWhiteSpace(station.TimeZoneId)) return Invalid("Station time zone is unavailable.");
        IReadOnlyList<Guid> allowedServiceIds = [];
        if (!isAdministrator)
        {
            var allowance = await resolver.AllowedServiceIdsForStaffAsync(access.Actor.StaffMemberId, flight.Station.StationId, ct);
            if (allowance.IsFailure) return allowance.Error;
            allowedServiceIds = allowance.Value;
        }
        var names = await FlightWorkAttribution.NamesAsync(userNames, FlightWorkAttribution.UserIds(flight).Append(userId), ct);
        FlightDetailDto Response()
        {
            if (isAdministrator)
                return FlightDetails.MapAdministrator(flight, station.TimeZoneId, names, canCorrect: true, currentUserId: userId);
            var actor = access.Actor;
            return request.PortalOnly
                ? FlightDetails.MapOperational(flight, station.TimeZoneId, actor, true, allowedServiceIds, names, portalOnly: true, canReview: PortalFlightScope.CanReview(user))
                : FlightDetails.Map(flight, station.TimeZoneId, actor.StaffMemberId, true, true, false, allowedServiceIds.Count > 0,
                    userId, allowedServiceIds, true, flight.CanCorrectOperationalWork(actor.StaffMemberId), names, true, legacyStaffAttribution: true);
        }
        var signatureCheck = ValidateSignature(input.Departure);
        if (signatureCheck.IsFailure) return signatureCheck.Error;
        long inlineBytes = input.Departure?.SignatureContent?.LongLength ?? 0;
        var matches = existing is not null && existing.ArrivalDetails.ArrivedAtUtc == input.Arrival.ArrivedAtUtc && existing.ArrivalDetails.Remarks == Normalize(input.Arrival.Remarks) &&
            existing.DepartureDetails?.DepartedAtUtc == input.Departure?.DepartedAtUtc && existing.DepartureDetails?.Remarks == Normalize(input.Departure?.Remarks) &&
            priorServices.Length == input.Services.Count && priorTasks.Length == input.Tasks.Count;
        var selectedReceiverId = isAdministrator ? input.Arrival.ReceivedByStaffMemberId : null;
        var selectedHandlerId = isAdministrator ? input.Departure?.HandledByStaffMemberId : null;
        if (existing is not null)
        {
            matches &= (selectedReceiverId is null || selectedReceiverId == existing.ArrivalDetails.ReceivedByEmployee.StaffMemberId) &&
                (selectedHandlerId is null || selectedHandlerId == existing.DepartureDetails?.HandledByEmployee.StaffMemberId);
            if (input.Departure?.RemoveSignature == true) matches &= existing.DepartureDetails?.CustomerSignature is null;
            else if (input.Departure?.SignatureContent is { } bytes)
                matches &= existing.DepartureDetails?.CustomerSignature is { } signature && signature.SignedAtUtc == input.Departure.SignatureSignedAtUtc && await SignatureMatches(signature.StorageKey, bytes, ct);
        }
        foreach (var change in input.Services)
        {
            var old = priorServices.SingleOrDefault(s => s.Id == change.RecordId);
            var valid = SaveFlightServiceCommandHandler.ValidateInput(change.Input, old); if (valid.IsFailure) return valid.Error;
            var same = old is not null && SaveFlightServiceCommandHandler.CoreMatches(old, change.Input) && old.Window.ToUtc == change.Input.EndUtc && await FlightServiceFiles.MatchesAsync(storage, old, change.Input.Attachments, ct);
            matches &= same;
            if (!isAdministrator && !same && !allowedServiceIds.Contains(change.Input.ServiceId)) return Error.Validation("This service is not allowed for your manpower type.", "Operations.WorkOrder.ServiceNotAllowed");
            foreach (var attachment in change.Input.Attachments ?? [])
            {
                inlineBytes += attachment.Content?.LongLength ?? 0;
                var retained = old?.Attachments.SingleOrDefault(a => a.Id == attachment.Id);
                if (retained is not null && attachment.Content is not null && !await FlightServiceFiles.ContentMatchesAsync(storage, retained, attachment.Content, ct)) return Immutable();
            }
        }
        foreach (var change in input.Tasks)
        {
            var old = priorTasks.SingleOrDefault(t => t.Id == change.RecordId);
            var valid = FlightTaskInputs.Validate(change.Input, old); if (valid.IsFailure) return valid.Error;
            matches &= old is not null && FlightTaskInputs.CoreMatches(old, change.Input) && old.Window.ToUtc == change.Input.EndUtc && await FlightTaskFiles.MatchesAsync(storage, old, change.Input.Attachments, ct);
            foreach (var attachment in change.Input.Attachments ?? [])
            {
                inlineBytes += attachment.Content?.LongLength ?? 0;
                var retained = old?.Attachments.SingleOrDefault(a => a.Id == attachment.Id);
                if (retained is not null && attachment.Content is not null && !await FlightTaskFiles.ContentMatchesAsync(storage, retained, attachment.Content, ct)) return Immutable();
            }
        }
        if (inlineBytes > FlightOperationsBatchPolicy.MaxInlineBytes) return Invalid("The combined RTR edit must contain at most 100 MB of new files.");
        if (matches) return Response();
        if (!flight.RowVersion.SequenceEqual(request.RowVersion)) return ConcurrencyErrors.Stale;
        var approvalLock = flight.EnsureApprovalAllowsReturnToRamp(request.OccurrenceId);
        if (approvalLock.IsFailure) return approvalLock.Error;
        if (isAdministrator &&
            (existing is { DepartureDetails: { } savedDeparture } && existing.CreatedByUserId != userId &&
                (input.Departure is null || input.Departure.RemoveSignature && savedDeparture.CustomerSignature is not null) ||
             input.Services.Any(change => priorServices.SingleOrDefault(item => item.Id == change.RecordId) is { } saved &&
                 saved.CreatedByUserId != userId && !PortalFlightRecordCorrections.RetainsSavedDetails(saved, change.Input)) ||
             input.Tasks.Any(change => priorTasks.SingleOrDefault(item => item.Id == change.RecordId) is { } saved &&
                 saved.CreatedByUserId != userId && !PortalFlightRecordCorrections.RetainsSavedDetails(saved, change.Input))))
            return PortalFlightRecordCorrections.SavedDetailsRemovalForbidden();
        if (request.PortalOnly && priorTasks.Any(task => task.CreatedByUserId != userId && input.Tasks.All(change => change.RecordId != task.Id)))
            return FlightTaskWrites.PortalDeleteReadOnly();
        var timeline = flight.ValidateReturnToRampTimeline(request.OccurrenceId, input.Arrival.ArrivedAtUtc,
            input.Departure?.DepartedAtUtc);
        if (timeline.IsFailure) return timeline.Error;
        if (await db.Flights.AsNoTracking().AnyAsync(f => f.Id != flight.Id && f.ReturnToRamps.Any(r => r.Id == request.OccurrenceId), ct)) return IdConflict();
        var identities = await ValidateIdentities(flight, request, ct); if (identities.IsFailure) return identities.Error;
        foreach (var deleted in priorServices.Where(old => input.Services.All(s => s.RecordId != old.Id)))
        {
            if (request.PortalOnly && deleted.CreatedByUserId != userId)
                return FlightServiceWrites.PortalDeleteReadOnly();
            if (!isAdministrator && !allowedServiceIds.Contains(deleted.Service.ServiceId)) return Error.Validation("This service is not allowed for your manpower type.", "Operations.WorkOrder.ServiceNotAllowed");
        }
        var receiver = existing?.ArrivalDetails.ReceivedByEmployee;
        var handler = existing?.DepartureDetails?.HandledByEmployee;
        if (isAdministrator)
        {
            var received = await resolver.ActualEmployeeAsync(selectedReceiverId, receiver, flight.Station.StationId, "receiving", ct);
            if (received.IsFailure) return received.Error;
            receiver = received.Value;
            if (input.Departure is not null)
            {
                var handled = await resolver.ActualEmployeeAsync(selectedHandlerId, handler, flight.Station.StationId, "handling", ct);
                if (handled.IsFailure) return handled.Error;
                handler = handled.Value;
            }
        }
        else
        {
            var staff = await resolver.StaffMembersForStationAsync([access.Actor.StaffMemberId], flight.Station.StationId, ct);
            if (staff.IsFailure) return staff.Error;
            var employee = staff.Value.Single();
            receiver ??= employee;
            handler ??= employee;
        }
        var arrival = RtrArrivalDetails.Create(input.Arrival.ArrivedAtUtc, receiver, input.Arrival.Remarks);
        if (arrival.IsFailure) return arrival.Error;
        var serviceInputs = new List<ReturnToRampServiceInput>();
        foreach (var change in input.Services)
        {
            var old = priorServices.SingleOrDefault(s => s.Id == change.RecordId); var value = change.Input;
            var snapshots = old?.Service.ServiceId == value.ServiceId ? Result.Success<IReadOnlyList<ServiceSnapshot>>([old.Service]) : await resolver.ServicesAsync([value.ServiceId], ct);
            if (snapshots.IsFailure) return snapshots.Error;
            var window = TimeWindow.Create(value.StartUtc, value.EndUtc); if (window.IsFailure) return window.Error;
            var newStaff = value.Performers.Where(p => old?.Performers.Any(o => o.Id == p.Id && o.Employee.StaffMemberId == p.StaffMemberId) != true).Select(p => p.StaffMemberId).ToArray();
            var staff = await resolver.StaffMembersForStationAsync(newStaff, flight.Station.StationId, ct); if (staff.IsFailure) return staff.Error;
            var people = new List<FlightServicePerformerInput>();
            foreach (var person in value.Performers)
            {
                var period = TimeWindow.Create(person.StartUtc, person.EndUtc); if (period.IsFailure) return period.Error;
                var retained = old?.Performers.SingleOrDefault(p => p.Id == person.Id && p.Employee.StaffMemberId == person.StaffMemberId);
                people.Add(new(retained?.Employee ?? staff.Value.Single(s => s.StaffMemberId == person.StaffMemberId), period.Value, person.Id));
            }
            serviceInputs.Add(new(change.RecordId, snapshots.Value.Single(), value.CustomerReferenceNumber, window.Value, value.Notes, people));
        }
        var taskInputs = new List<ReturnToRampTaskInput>();
        foreach (var change in input.Tasks)
        {
            var value = change.Input; var old = priorTasks.SingleOrDefault(t => t.Id == change.RecordId);
            var data = await FlightTaskInputs.ResolveAsync(value, old, flight.Station.StationId, masterData, resolver, ct); if (data.IsFailure) return data.Error;
            taskInputs.Add(new(change.RecordId, (FlightTaskType)value.TaskType, data.Value.AtaChapter, value.CustomerReferenceNumber, data.Value.Window, value.Notes, data.Value.Performers, data.Value.Tools, data.Value.Materials, data.Value.GeneralSupports));
        }
        var createdKeys = new List<string>();
        var oldKeys = priorServices.SelectMany(s => s.Attachments.Select(a => a.Details.StorageKey)).Concat(priorTasks.SelectMany(t => t.Attachments.Select(a => a.Details.StorageKey))).ToList();
        if (existing?.DepartureDetails?.CustomerSignature is { } previousSignature) oldKeys.Add(previousSignature.StorageKey);
        try
        {
            var signature = input.Departure?.RemoveSignature == true ? null : existing?.DepartureDetails?.CustomerSignature;
            if (input.Departure?.SignatureContent is { } content)
            {
                using var stream = new MemoryStream(content, false);
                var stored = await storage.SaveAsync("flight-rtr-signatures", "customer-signature.png", "image/png", stream, ct); createdKeys.Add(stored.StorageKey);
                var result = CustomerSignature.Create("customer-signature.png", stored.StorageKey, stored.ContentType, stored.SizeBytes, input.Departure.SignatureSignedAtUtc!.Value);
                if (result.IsFailure) return result.Error; signature = result.Value;
            }
            RtrDepartureDetails? departure = null;
            if (input.Departure is { } end)
            {
                var result = RtrDepartureDetails.Create(end.DepartedAtUtc, signature, handler, end.Remarks);
                if (result.IsFailure) return result.Error; departure = result.Value;
            }
            var saved = flight.SaveReturnToRamp(request.OccurrenceId, arrival.Value, departure, serviceInputs, taskInputs, userId, clock.GetUtcNow());
            if (saved.IsFailure) return saved.Error;
            foreach (var change in input.Services)
            {
                var record = flight.ServiceRecords.Single(s => s.Id == change.RecordId);
                if (change.Input.Attachments is not { } desired) continue;
                foreach (var removed in record.Attachments.Where(a => desired.All(d => d.Id != a.Id)).ToArray())
                { var result = flight.RemoveServiceAttachment(record.Id, removed.Id, userId, clock.GetUtcNow(), request.OccurrenceId); if (result.IsFailure) return result.Error; }
                foreach (var added in desired.Where(a => record.Attachments.All(old => old.Id != a.Id)))
                {
                    var details = await StoreAttachment(added, createdKeys, ct); if (details.IsFailure) return details.Error;
                    var result = flight.AddServiceAttachment(record.Id, details.Value, userId, clock.GetUtcNow(), request.OccurrenceId, added.Id); if (result.IsFailure) return result.Error;
                }
            }
            foreach (var change in input.Tasks)
            {
                var record = flight.TaskRecords.Single(t => t.Id == change.RecordId);
                if (change.Input.Attachments is not { } desired) continue;
                foreach (var removed in record.Attachments.Where(a => desired.All(d => d.Id != a.Id)).ToArray())
                { var result = flight.RemoveTaskAttachment(record.Id, removed.Id, userId, clock.GetUtcNow(), request.OccurrenceId); if (result.IsFailure) return result.Error; }
                foreach (var added in desired.Where(a => record.Attachments.All(old => old.Id != a.Id)))
                {
                    var details = await StoreAttachment(added, createdKeys, ct); if (details.IsFailure) return details.Error;
                    var result = flight.AddTaskAttachment(record.Id, details.Value, userId, clock.GetUtcNow(), request.OccurrenceId, added.Id); if (result.IsFailure) return result.Error;
                }
            }
            if (existing is not null && existing.CreatedByUserId != userId)
                db.Enqueue(new global::Operations.Contracts.FlightSubmissionUpdated
                {
                    FlightId = flight.Id, FlightNumber = flight.FlightNumber.Value, SubmissionId = existing.Id,
                    SubmissionKind = global::Operations.Contracts.FlightSubmissionKind.ReturnToRamp,
                    SubmittedByStaffMemberId = null, SubmittedByUserId = existing.CreatedByUserId,
                    UpdatedByStaffMemberId = access.StaffActor?.StaffMemberId, UpdatedByUserId = userId, UpdatedByDisplayName = names.GetValueOrDefault(userId) ?? "", OccurredOnUtc = clock.GetUtcNow()
                });
            db.SetOriginalRowVersion(flight, request.RowVersion);
            if (!isAdministrator)
                await FlightActivityRecorder.TouchAsync(db, flight, access.Actor, clock.GetUtcNow(), ct, recordSearch: !request.PortalOnly);
            await db.SaveChangesAsync(ct);
            FlightServiceWrites.Notify(mobileSync, clock);
            return Response();
        }
        catch (DbUpdateConcurrencyException) { return ConcurrencyErrors.Stale; }
        catch (DbUpdateException)
        {
            // A competing RTR can hit the open-occurrence or sequence index before the flight's rowversion check.
            var persisted = await db.Flights.AsNoTracking().Where(item => item.Id == flight.Id)
                .Select(item => item.RowVersion).SingleOrDefaultAsync(ct);
            if (persisted is not null && !persisted.SequenceEqual(request.RowVersion)) return ConcurrencyErrors.Stale;
            if (existing is null &&
                await db.Flights.AsNoTracking().AnyAsync(f => f.ReturnToRamps.Any(r => r.Id == request.OccurrenceId), ct))
                return ConcurrencyErrors.Stale;
            throw;
        }
        finally { await FlightServiceFiles.DeleteUnreferencedAsync(db, storage, createdKeys.Concat(oldKeys)); }
    }

    private async Task<Result<AttachmentDetails>> StoreAttachment(SaveFlightServiceAttachmentRequest input, List<string> keys, CancellationToken ct)
    {
        using var stream = new MemoryStream(input.Content!, false);
        var stored = await storage.SaveAsync("flight-rtr-attachments", input.FileName, input.ContentType, stream, ct); keys.Add(stored.StorageKey);
        return AttachmentDetails.Create(input.FileName, stored.StorageKey, stored.ContentType, stored.SizeBytes, (AttachmentKind)input.Kind);
    }
    private async Task<bool> SignatureMatches(string key, byte[] bytes, CancellationToken ct)
    {
        await using var content = await storage.OpenAsync(key, ct); if (content is null) return false;
        using var copy = new MemoryStream(); await content.CopyToAsync(copy, ct); return copy.ToArray().AsSpan().SequenceEqual(bytes);
    }
    private static Result ValidateSignature(SaveReturnToRampDepartureRequest? input)
    {
        if (input is null) return Result.Success();
        if (input.DepartedAtUtc == default || input.RemoveSignature && input.SignatureContent is not null) return Invalid("A valid RTR departure and signature selection are required.");
        if (input.SignatureContent is { } content)
        {
            if (content.Length > SaveFlightDepartureCommandHandler.MaxSignatureBytes || !SaveFlightDepartureCommandHandler.IsPng(content)) return Invalid("Customer signature must be a PNG of at most 2 MB.");
            if (input.SignatureSignedAtUtc is null || input.SignatureSignedAtUtc == default(DateTimeOffset)) return Invalid("Customer signing time is required.");
        }
        else if (input.SignatureSignedAtUtc is not null) return Invalid("Signing time requires new signature content.");
        return Result.Success();
    }
    private async Task<Result> ValidateIdentities(Flight flight, SaveFlightReturnToRampCommand request, CancellationToken ct)
    {
        var services = request.Input.Services!; var tasks = request.Input.Tasks!;
        var serviceChildIds = services.SelectMany(s => s.Input.Performers.Select(p => p.Id)).ToArray();
        var taskChildIds = tasks.SelectMany(t => t.Input.Performers.Select(p => p.Id)).ToArray();
        if (serviceChildIds.Distinct().Count() != serviceChildIds.Length || taskChildIds.Distinct().Count() != taskChildIds.Length) return IdConflict();
        static bool Repeated(IEnumerable<Guid> ids) { var list = ids.ToArray(); return list.Distinct().Count() != list.Length; }
        if (Repeated(services.SelectMany(s => s.Input.Attachments?.Select(a => a.Id) ?? [])) ||
            Repeated(tasks.SelectMany(t => t.Input.Attachments?.Select(a => a.Id) ?? [])) ||
            Repeated(tasks.SelectMany(t => t.Input.Tools.Select(a => a.Id))) ||
            Repeated(tasks.SelectMany(t => t.Input.Materials.Select(a => a.Id))) ||
            Repeated(tasks.SelectMany(t => t.Input.GeneralSupports.Select(a => a.Id)))) return IdConflict();
        foreach (var change in services)
        {
            var people = change.Input.Performers.Select(p => p.Id).ToArray(); var files = change.Input.Attachments?.Select(a => a.Id).ToArray() ?? [];
            if (await db.Flights.AsNoTracking().AnyAsync(f => f.ServiceRecords.Any(s =>
                (s.Id == change.RecordId && (f.Id != flight.Id || s.ReturnToRampId != request.OccurrenceId)) ||
                (f.Id != flight.Id || s.Id != change.RecordId) && (s.Performers.Any(p => people.Contains(p.Id)) || s.Attachments.Any(a => files.Contains(a.Id)))), ct)) return IdConflict();
        }
        foreach (var change in tasks)
        {
            var value = change.Input; var people = value.Performers.Select(p => p.Id).ToArray(); var files = value.Attachments?.Select(a => a.Id).ToArray() ?? [];
            var tools = value.Tools.Select(p => p.Id).ToArray(); var materials = value.Materials.Select(p => p.Id).ToArray(); var supports = value.GeneralSupports.Select(p => p.Id).ToArray();
            if (await db.Flights.AsNoTracking().AnyAsync(f => f.TaskRecords.Any(t =>
                (t.Id == change.RecordId && (f.Id != flight.Id || t.ReturnToRampId != request.OccurrenceId)) ||
                (f.Id != flight.Id || t.Id != change.RecordId) && (t.Performers.Any(p => people.Contains(p.Id)) || t.Attachments.Any(a => files.Contains(a.Id)) ||
                 t.Tools.Any(p => tools.Contains(p.Id)) || t.Materials.Any(p => materials.Contains(p.Id)) || t.GeneralSupports.Any(p => supports.Contains(p.Id)))), ct)) return IdConflict();
        }
        return Result.Success();
    }
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static Error Invalid(string message) => Error.Validation(message, "Operations.ReturnToRamp.RequestInvalid");
    private static Error Immutable() => Error.Validation("Use a new attachment ID to replace a file.", "Operations.FlightService.AttachmentImmutable");
    private static Error IdConflict() => Error.Conflict("A return-to-ramp work or child record identity is already in use.", "Operations.ReturnToRamp.IdConflict");
}
