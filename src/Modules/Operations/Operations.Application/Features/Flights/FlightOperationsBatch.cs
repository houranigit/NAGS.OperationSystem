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

public sealed record SaveFlightOperationsBatchCommand(Guid FlightId, SaveFlightOperationsBatchRequest Input, byte[] RowVersion)
    : ICommand<FlightDetailDto>;

/// <summary>Prepares one flight edit in memory and publishes its entire graph and outbox in one SaveChanges.</summary>
public sealed class SaveFlightOperationsBatchCommandHandler(
    IOperationsDbContext db, IMobileFlightScope scope, MasterDataResolver resolver, IMasterDataReader masterData,
    TimeProvider clock, IMobileSyncBroadcaster mobileSync, IFileStorage storage, IUserDisplayNameReader? userNames = null)
    : ICommandHandler<SaveFlightOperationsBatchCommand, FlightDetailDto>
{
    private sealed record ServiceEdit(FlightServiceBatchChange Change, FlightService? Existing, bool Matches,
        ServiceSnapshot? Snapshot = null, TimeWindow? Window = null, IReadOnlyList<FlightServicePerformerInput>? Performers = null, bool ClosureOnly = false);
    private sealed record TaskEdit(FlightTaskBatchChange Change, FlightTask? Existing, bool Matches,
        ResolvedFlightTaskInput? Resolved = null);

    public async Task<Result<FlightDetailDto>> Handle(SaveFlightOperationsBatchCommand request, CancellationToken ct)
    {
        if (request.FlightId == Guid.Empty || request.RowVersion is not { Length: 8 } || request.Input is null)
            return Invalid("A flight edit and current version are required.");
        var input = request.Input;
        var services = input.ServiceChanges ?? [];
        var tasks = input.TaskChanges ?? [];
        if (services.Any(item => item is null || item.RecordId == Guid.Empty) ||
            tasks.Any(item => item is null || item.RecordId == Guid.Empty) ||
            services.Select(item => item.RecordId).Distinct().Count() != services.Count ||
            tasks.Select(item => item.RecordId).Distinct().Count() != tasks.Count)
            return Invalid("Work record IDs must be present and unique within each change list.");
        long inlineBytes = input.Departure?.SignatureContent?.LongLength ?? 0;
        foreach (var attachments in services.Select(item => item.Input?.Attachments)
            .Concat(tasks.Select(item => item.Input?.Attachments)))
            inlineBytes += attachments?.Sum(item => item?.Content?.LongLength ?? 0) ?? 0;
        if (inlineBytes > FlightOperationsBatchPolicy.MaxInlineBytes)
            return Error.Validation("The combined flight edit must contain at most 100 MB of new file content.",
                "Operations.FlightBatch.InlineFilesTooLarge");

        var resolvedScope = await scope.ResolveAsync(ct);
        if (resolvedScope.IsFailure) return resolvedScope.Error;
        var actor = resolvedScope.Value;
        var flight = await FlightServiceWrites.Load(db, actor).SingleOrDefaultAsync(f => f.Id == request.FlightId, ct);
        if (flight is null) return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");
        var station = await masterData.GetStationAsync(flight.Station.StationId, ct);
        if (station is null || string.IsNullOrWhiteSpace(station.TimeZoneId))
            return Error.Failure("Flight data could not be loaded. Please try again.", "Operations.Flight.StationDataUnavailable");
        var assigned = flight.CanCorrectOperationalWork(actor.StaffMemberId);
        var canAddWork = FlightSearchPolicy.CanAddWork(flight, actor.StaffMemberId);
        if ((services.Count > 0 || tasks.Count > 0) && flight.CancellationSubmissions.Count > 0)
            return FlightServiceWrites.Cancellation();
        if ((input.Arrival is not null || input.Departure is not null) &&
            flight.CancellationSubmissions.Any(item => item.SubmittedByEmployee.StaffMemberId == actor.StaffMemberId))
            return Error.Conflict("Revert your cancellation before saving work details.", "Operations.Flight.OwnCancellationSubmitted");

        var arrivalTargetResult = ArrivalTarget(flight, input.Arrival, actor, assigned);
        if (arrivalTargetResult.IsFailure) return arrivalTargetResult.Error;
        var arrivalTarget = arrivalTargetResult.Value;
        var departureTargetResult = DepartureTarget(flight, input.Departure, actor, assigned);
        if (departureTargetResult.IsFailure) return departureTargetResult.Error;
        var departureTarget = departureTargetResult.Value;
        if (input.Arrival is not null)
        {
            var arrivalAccess = FlightSearchPolicy.Arrival(flight, actor.StaffMemberId, arrivalTarget);
            if (arrivalAccess.IsFailure) return arrivalAccess.Error;
        }
        var arrivalMatches = input.Arrival is null || ArrivalMatches(arrivalTarget, input.Arrival, actor.StaffMemberId);
        var departureShape = ValidateDeparture(input.Departure, departureTarget);
        if (departureShape.IsFailure) return departureShape.Error;
        var departureMatches = input.Departure is null || await DepartureMatchesAsync(departureTarget, input.Departure, actor.StaffMemberId, ct);
        if (input.Arrival is { } arrival && (arrival.Ata == default || arrival.AircraftTypeId == Guid.Empty || string.IsNullOrWhiteSpace(arrival.AircraftTailNumber)))
            return Invalid("Arrival time, aircraft type and tail number are required.");

        var preparedServices = new List<ServiceEdit>();
        foreach (var change in services)
        {
            var existing = flight.ServiceRecords.SingleOrDefault(item => item.Id == change.RecordId);
            if (existing?.ReturnToRampId is not null) return FlightServiceWrites.NotFound();
            if (change.Input is not { } service)
            {
                if (existing is not null && !assigned &&
                    (existing.CreatedByUserId != actor.UserId || existing.Window.ToUtc is not null && existing.EndedByUserId != actor.UserId))
                    return Error.Forbidden("Only its creator or an assigned employee can delete this service.", "Operations.FlightService.DeleteReadOnly");
                if (existing is not null)
                {
                    var eligibility = await resolver.EnsurePerformedServiceAllowedAsync(existing.Service.ServiceId, actor.StaffMemberId, flight.Station.StationId, ct);
                    if (eligibility.IsFailure) return eligibility.Error;
                }
                preparedServices.Add(new(change, existing, existing is null));
                continue;
            }
            if (existing is null && !canAddWork) return Error.Forbidden(
                "Only existing open work can be completed on this unassigned flight.", "Operations.Search.NewWorkReadOnly");
            var validation = SaveFlightServiceCommandHandler.ValidateInput(service, existing);
            if (validation.IsFailure) return validation.Error;
            foreach (var attachment in service.Attachments ?? [])
            {
                var retained = existing?.Attachments.SingleOrDefault(item => item.Id == attachment.Id);
                if (retained is not null && attachment.Content is not null &&
                    !await FlightServiceFiles.ContentMatchesAsync(storage, retained, attachment.Content, ct))
                    return ImmutableAttachment();
            }
            var coreMatches = existing is not null && SaveFlightServiceCommandHandler.CoreMatches(existing, service);
            var mediaMatches = existing is not null && await FlightServiceFiles.MatchesAsync(storage, existing, service.Attachments, ct);
            var matches = coreMatches && existing!.Window.ToUtc == service.EndUtc && mediaMatches;
            var closureOnly = existing is not null && FlightSearchPolicy.IsUnassignedRegular(flight, actor.StaffMemberId) &&
                FlightSearchPolicy.ServiceClosure(existing, service, mediaMatches);
            if (!matches && existing is not null && FlightSearchPolicy.IsUnassignedRegular(flight, actor.StaffMemberId) &&
                existing.CreatedByUserId != actor.UserId && !closureOnly) return FlightSearchPolicy.ClosureReadOnly();
            if (!FlightSearchPolicy.BypassServiceAllowance(flight, actor, existing, service, matches, mediaMatches))
            {
                var eligibility = await resolver.EnsurePerformedServiceAllowedAsync(service.ServiceId, actor.StaffMemberId, flight.Station.StationId, ct);
                if (eligibility.IsFailure) return eligibility.Error;
            }
            if (existing is not null && !matches)
            {
                if ((!coreMatches || !mediaMatches) && existing.CreatedByUserId != actor.UserId && !assigned && !closureOnly)
                    return Error.Forbidden("Only its creator or an assigned employee can change this service.", "Operations.FlightService.ReadOnly");
                if (existing.Window.ToUtc != service.EndUtc && existing.Window.ToUtc is not null && existing.EndedByUserId != actor.UserId && !assigned)
                    return Error.Forbidden("Only its recorded closer or an assigned employee can change End.", "Operations.FlightService.EndReadOnly");
                if (existing.Service.ServiceId != service.ServiceId && existing.HasOtherUserEnd && !assigned)
                    return Error.Forbidden("The selected service cannot change after another employee records End.", "Operations.FlightService.SelectionReadOnly");
            }
            preparedServices.Add(new(change, existing, matches, ClosureOnly: closureOnly));
        }
        var preparedTasks = new List<TaskEdit>();
        foreach (var raw in tasks)
        {
            var change = raw with { Input = raw.Input is null ? null : raw.Input with
                { Attachments = raw.Input.Attachments?.Select(item => item is null ? null! : item with { ContentType = NormalizeMime(item.ContentType) }).ToList() } };
            var existing = flight.TaskRecords.SingleOrDefault(item => item.Id == change.RecordId);
            if (existing?.ReturnToRampId is not null) return FlightTaskWrites.NotFound();
            if (change.Input is not { } task)
            {
                if (existing is not null && !assigned &&
                    (existing.CreatedByUserId != actor.UserId || existing.Window.ToUtc is not null && existing.EndedByUserId != actor.UserId))
                    return Error.Forbidden("Only its creator or an assigned employee can delete this task.", "Operations.FlightTask.DeleteReadOnly");
                preparedTasks.Add(new(change, existing, existing is null));
                continue;
            }
            if (existing is null && !canAddWork) return Error.Forbidden(
                "Only existing open work can be completed on this unassigned flight.", "Operations.Search.NewWorkReadOnly");
            var validation = FlightTaskInputs.Validate(task, existing);
            if (validation.IsFailure) return validation.Error;
            foreach (var attachment in task.Attachments ?? [])
            {
                var retained = existing?.Attachments.SingleOrDefault(item => item.Id == attachment.Id);
                if (retained is not null && attachment.Content is not null &&
                    !await FlightTaskFiles.ContentMatchesAsync(storage, retained, attachment.Content, ct))
                    return ImmutableAttachment();
            }
            var coreMatches = existing is not null && FlightTaskInputs.CoreMatches(existing, task);
            var mediaMatches = existing is not null && await FlightTaskFiles.MatchesAsync(storage, existing, task.Attachments, ct);
            var matches = coreMatches && existing!.Window.ToUtc == task.EndUtc && mediaMatches;
            var closureOnly = existing is not null && FlightSearchPolicy.IsUnassignedRegular(flight, actor.StaffMemberId) &&
                FlightSearchPolicy.TaskClosure(existing, task, mediaMatches);
            if (!matches && existing is not null && FlightSearchPolicy.IsUnassignedRegular(flight, actor.StaffMemberId) &&
                existing.CreatedByUserId != actor.UserId && !closureOnly) return FlightSearchPolicy.ClosureReadOnly();
            if (existing is not null && !matches)
            {
                if ((!coreMatches || !mediaMatches) && existing.CreatedByUserId != actor.UserId && !assigned && !closureOnly)
                    return Error.Forbidden("Only its creator or an assigned employee can change this task.", "Operations.FlightTask.ReadOnly");
                if (existing.Window.ToUtc != task.EndUtc && existing.Window.ToUtc is not null && existing.EndedByUserId != actor.UserId && !assigned)
                    return Error.Forbidden("Only its recorded closer or an assigned employee can change End.", "Operations.FlightTask.EndReadOnly");
            }
            preparedTasks.Add(new(change, existing, matches));
        }
        var allowed = await resolver.AllowedServiceIdsForStaffAsync(actor.StaffMemberId, flight.Station.StationId, ct);
        var allowedIds = allowed.IsSuccess ? allowed.Value : [];
        var names = await FlightWorkAttribution.NamesAsync(userNames,
            FlightWorkAttribution.UserIds(flight).Append(actor.UserId), ct);
        FlightDetailDto Response() => Map(flight, station.TimeZoneId, actor, assigned, allowedIds, names);
        // Recover only a complete prior success. Never acknowledge individual members of a stale batch.
        if (arrivalMatches && departureMatches && preparedServices.All(item => item.Matches) && preparedTasks.All(item => item.Matches))
            return Response();
        if (!flight.RowVersion.SequenceEqual(request.RowVersion)) return ConcurrencyErrors.Stale;
        if ((!arrivalMatches && input.Arrival?.AcknowledgeConflictingSubmission != true ||
            !departureMatches && input.Departure?.AcknowledgeConflictingSubmission != true) &&
            flight.CancellationSubmissions.Any(item => item.SubmittedByEmployee.StaffMemberId != actor.StaffMemberId))
            return Error.Conflict("Another employee reported this flight as canceled. Confirm before saving work details.",
                "Operations.Flight.CancellationConfirmationRequired");
        var identityCheck = await ValidateIdentitiesAsync(flight.Id, preparedServices, preparedTasks, ct);
        if (identityCheck.IsFailure) return identityCheck.Error;

        if (!arrivalMatches && input.Arrival is { } changedArrival)
        {
            var remarks = FlightActualRules.ArrivalRemarks(flight, changedArrival.Remarks);
            if (remarks.IsFailure) return remarks.Error;
        }

        // Resolve every reference and value before staging files or publishing any graph changes.
        for (var index = 0; index < preparedServices.Count; index++)
        {
            var edit = preparedServices[index];
            if (edit.Matches || edit.Change.Input is not { } service) continue;
            var snapshots = edit.ClosureOnly ? Result.Success<IReadOnlyList<ServiceSnapshot>>([edit.Existing!.Service])
                : await resolver.ServicesAsync([service.ServiceId], ct);
            if (snapshots.IsFailure) return snapshots.Error;
            var window = TimeWindow.Create(service.StartUtc, service.EndUtc);
            if (window.IsFailure) return window.Error;
            var staffIds = service.Performers.Where(item => edit.Existing?.Performers.Any(old => old.Id == item.Id && old.Employee.StaffMemberId == item.StaffMemberId) != true)
                .Select(item => item.StaffMemberId).ToArray();
            var staff = await resolver.StaffMembersForStationAsync(staffIds, flight.Station.StationId, ct);
            if (staff.IsFailure) return staff.Error;
            var performers = new List<FlightServicePerformerInput>();
            foreach (var person in service.Performers)
            {
                var period = TimeWindow.Create(person.StartUtc, person.EndUtc);
                if (period.IsFailure) return period.Error;
                var retained = edit.Existing?.Performers.SingleOrDefault(old => old.Id == person.Id && old.Employee.StaffMemberId == person.StaffMemberId);
                performers.Add(new(retained?.Employee ?? staff.Value.Single(item => item.StaffMemberId == person.StaffMemberId), period.Value, person.Id));
            }
            var snapshot = edit.Existing is not null && edit.Existing.CreatedByUserId != actor.UserId && edit.Existing.Service.ServiceId == service.ServiceId
                ? edit.Existing.Service : snapshots.Value.Single();
            preparedServices[index] = edit with { Snapshot = snapshot, Window = window.Value, Performers = performers };
        }
        for (var index = 0; index < preparedTasks.Count; index++)
        {
            var edit = preparedTasks[index];
            if (edit.Matches || edit.Change.Input is not { } task) continue;
            var details = await FlightTaskInputs.ResolveAsync(task, edit.Existing, flight.Station.StationId, masterData, resolver, ct);
            if (details.IsFailure) return details.Error;
            preparedTasks[index] = edit with { Resolved = details.Value };
        }
        StaffMemberSnapshot? employee = null;
        ArrivalDetails? arrivalDetails = null;
        DepartureDetails? departureDetails = null;
        if (!arrivalMatches || !departureMatches)
        {
            var staff = await resolver.StaffMembersForStationAsync([actor.StaffMemberId], flight.Station.StationId, ct);
            if (staff.IsFailure) return staff.Error;
            employee = staff.Value.Single();
        }
        if (!arrivalMatches && input.Arrival is { } arrivalInput)
        {
            var aircraft = await resolver.AircraftTypeAsync(arrivalInput.AircraftTypeId, ct);
            if (aircraft.IsFailure) return aircraft.Error;
            var details = ArrivalDetails.Create(arrivalInput.CustomerWorkReferenceNumber, arrivalInput.Ata, aircraft.Value,
                arrivalInput.AircraftTailNumber,
                arrivalTarget is not null && arrivalTarget.SubmittedByEmployee.StaffMemberId != actor.StaffMemberId
                    ? arrivalTarget.Details.ReceivedByEmployee : employee, arrivalInput.Remarks);
            if (details.IsFailure) return details.Error;
            arrivalDetails = details.Value;
        }
        if (!departureMatches && input.Departure is { } departureInput)
        {
            var signature = departureTarget?.Details.CustomerSignature;
            if (departureInput.SignatureContent is { } bytes)
            {
                // Metadata-only placeholder validates the final graph before file storage is touched.
                var value = CustomerSignature.Create("customer-signature.png", "validation", "image/png", bytes.LongLength,
                    departureInput.SignatureSignedAtUtc!.Value);
                if (value.IsFailure) return value.Error;
                signature = value.Value;
            }
            var details = DepartureDetails.Create(departureInput.Atd, signature,
                departureTarget is not null && departureTarget.SubmittedByEmployee.StaffMemberId != actor.StaffMemberId
                    ? departureTarget.Details.HandledByEmployee : employee, departureInput.Remarks);
            if (details.IsFailure) return details.Error;
            departureDetails = details.Value;
        }

        var now = clock.GetUtcNow();
        var arrivalTimeChanged = arrivalDetails is not null && arrivalTarget?.Details.Ata != arrivalDetails.Ata;
        var departureTimeChanged = departureDetails is not null && departureTarget?.Details.Atd != departureDetails.Atd;
        var previousSignatureKey = departureTarget?.Details.CustomerSignature?.StorageKey;
        FlightArrivalSubmission? savedArrival = null;
        FlightDepartureSubmission? savedDeparture = null;
        if (arrivalDetails is not null)
        {
            var saved = flight.SubmitArrival(employee, arrivalDetails, actor.UserId, now, arrivalTarget?.Id);
            if (saved.IsFailure) return saved.Error;
            savedArrival = saved.Value;
        }
        if (departureDetails is not null)
        {
            var saved = flight.SubmitDeparture(employee, departureDetails, actor.UserId, now, departureTarget?.Id);
            if (saved.IsFailure) return saved.Error;
            savedDeparture = saved.Value;
        }
        var removedKeys = new List<string>();
        foreach (var edit in preparedServices.Where(item => !item.Matches))
        {
            if (edit.Change.Input is not { } service)
            {
                removedKeys.AddRange(edit.Existing!.Attachments.Select(item => item.Details.StorageKey));
                var removed = flight.RemoveService(edit.Change.RecordId, actor.UserId, now, assignedEditorStaffMemberId: assigned ? actor.StaffMemberId : null);
                if (removed.IsFailure) return removed.Error;
            }
            else
            {
                var saved = flight.SaveService(edit.Change.RecordId, edit.Snapshot, service.CustomerReferenceNumber, edit.Window,
                    service.Notes, edit.Performers!, actor.UserId, now, assigned ? actor.StaffMemberId : null);
                if (saved.IsFailure) return saved.Error;
            }
        }
        foreach (var edit in preparedTasks.Where(item => !item.Matches))
        {
            if (edit.Change.Input is not { } task)
            {
                removedKeys.AddRange(edit.Existing!.Attachments.Select(item => item.Details.StorageKey));
                var removed = flight.RemoveTask(edit.Change.RecordId, actor.UserId, now, assignedEditorStaffMemberId: assigned ? actor.StaffMemberId : null);
                if (removed.IsFailure) return removed.Error;
            }
            else
            {
                var value = edit.Resolved!;
                var saved = flight.SaveTask(edit.Change.RecordId, (FlightTaskType)task.TaskType, value.AtaChapter, task.CustomerReferenceNumber,
                    value.Window, task.Notes, value.Performers, value.Tools, value.Materials, value.GeneralSupports, actor.UserId, now);
                if (saved.IsFailure) return saved.Error;
            }
        }
        // These guards run against the completed desired graph, not an intermediate request order.
        if (preparedServices.Any(item => !item.Matches && item.Change.Input is not null) ||
            preparedTasks.Any(item => !item.Matches && item.Change.Input is not null))
        {
            var arrivalRequired = FlightActualRules.RequireArrival(flight, actor.StaffMemberId);
            if (arrivalRequired.IsFailure) return arrivalRequired.Error;
            var chronology = FlightActualRules.Chronology(flight, actor.StaffMemberId);
            if (chronology.IsFailure) return chronology.Error;
        }
        if (departureDetails is not null)
        {
            var actualsStaffId = savedDeparture!.SubmittedByEmployee.StaffMemberId;
            var arrivalRequired = FlightActualRules.RequireArrival(flight, actualsStaffId);
            if (arrivalRequired.IsFailure) return arrivalRequired.Error;
            var chronology = FlightActualRules.Chronology(flight, actualsStaffId, departureOverride: departureDetails.Atd);
            if (chronology.IsFailure) return chronology.Error;
        }
        if (arrivalDetails is not null)
        {
            var chronology = FlightActualRules.Chronology(flight, savedArrival!.SubmittedByEmployee.StaffMemberId, arrivalOverride: arrivalDetails.Ata);
            if (chronology.IsFailure) return chronology.Error;
        }
        var overlaps = FlightActualRules.ServicePeriods(flight,
            preparedServices.Where(item => !item.Matches && item.Change.Input is not null).Select(item => item.Change.RecordId).ToHashSet());
        if (overlaps.IsFailure) return overlaps.Error;
        foreach (var edit in preparedServices.Where(item => !item.Matches && item.Change.Input is not null))
        {
            var bounds = FlightServiceBounds.Validate(flight, edit.Change.Input!, actor.StaffMemberId);
            if (bounds.IsFailure) return bounds.Error;
            var completion = FlightServiceBounds.RequireCompletedIfDeparted(flight, edit.Change.Input!, actor.StaffMemberId);
            if (completion.IsFailure) return completion.Error;
            var closed = FlightServiceBounds.RequireClosedPerformers(edit.Change.Input!);
            if (closed.IsFailure) return closed.Error;
        }
        foreach (var edit in preparedTasks.Where(item => !item.Matches && item.Change.Input is not null))
        {
            var bounds = FlightTaskBounds.Validate(flight, edit.Change.Input!, actor.StaffMemberId);
            if (bounds.IsFailure) return bounds.Error;
            var completion = FlightTaskBounds.RequireCompletedIfDeparted(flight, edit.Change.Input!, actor.StaffMemberId);
            if (completion.IsFailure) return completion.Error;
            var closed = FlightTaskBounds.RequireClosedPeriods(edit.Change.Input!);
            if (closed.IsFailure) return closed.Error;
        }
        if (arrivalTimeChanged)
        {
            var staffId = savedArrival!.SubmittedByEmployee.StaffMemberId;
            var bounds = FlightServiceBounds.ValidateExistingServices(flight, staffId, arrivalOverride: arrivalDetails!.Ata);
            if (bounds.IsFailure) return bounds.Error;
            var taskBounds = FlightTaskBounds.ValidateExistingTasks(flight, staffId, arrivalOverride: arrivalDetails.Ata);
            if (taskBounds.IsFailure) return taskBounds.Error;
        }
        if (departureTimeChanged)
        {
            var staffId = savedDeparture!.SubmittedByEmployee.StaffMemberId;
            var bounds = FlightServiceBounds.ValidateExistingServices(flight, staffId, departureOverride: departureDetails!.Atd);
            if (bounds.IsFailure) return bounds.Error;
            var taskBounds = FlightTaskBounds.ValidateExistingTasks(flight, staffId, departureOverride: departureDetails.Atd);
            if (taskBounds.IsFailure) return taskBounds.Error;
        }
        if (departureDetails is not null)
        {
            var complete = FlightServiceBounds.RequireCompletedForDeparture(flight);
            if (complete.IsFailure) return complete.Error;
            var taskComplete = FlightTaskBounds.RequireCompletedForDeparture(flight);
            if (taskComplete.IsFailure) return taskComplete.Error;
        }

        var createdKeys = new List<string>();
        try
        {
            foreach (var edit in preparedServices.Where(item => !item.Matches && item.Change.Input?.Attachments is not null))
            {
                var files = await ApplyServiceFilesAsync(flight, edit.Change.RecordId, edit.Change.Input!.Attachments!, actor.UserId, now, createdKeys, removedKeys, ct);
                if (files.IsFailure) return files.Error;
            }
            foreach (var edit in preparedTasks.Where(item => !item.Matches && item.Change.Input?.Attachments is not null))
            {
                var files = await ApplyTaskFilesAsync(flight, edit.Change.RecordId, edit.Change.Input!.Attachments!, actor.UserId, now, createdKeys, removedKeys, ct);
                if (files.IsFailure) return files.Error;
            }
            if (savedDeparture is not null && input.Departure?.SignatureContent is { } signatureBytes)
            {
                using var stream = new MemoryStream(signatureBytes, writable: false);
                var stored = await storage.SaveAsync("flight-departure-signatures", "customer-signature.png", "image/png", stream, ct);
                createdKeys.Add(stored.StorageKey);
                var signature = CustomerSignature.Create("customer-signature.png", stored.StorageKey, stored.ContentType, stored.SizeBytes, input.Departure.SignatureSignedAtUtc!.Value);
                if (signature.IsFailure) return signature.Error;
                var details = DepartureDetails.Create(departureDetails!.Atd, signature.Value, departureDetails.HandledByEmployee, departureDetails.Remarks);
                if (details.IsFailure) return details.Error;
                var updated = flight.SubmitDeparture(employee, details.Value, actor.UserId, now, savedDeparture.Id);
                if (updated.IsFailure) return updated.Error;
                if (previousSignatureKey is not null) removedKeys.Add(previousSignatureKey);
            }
            void Notify(Guid id, Guid creator, global::Operations.Contracts.FlightSubmissionKind kind,
                Guid? submittedStaffId = null, bool deleted = false)
            {
                if (creator == actor.UserId) return;
                db.Enqueue(new global::Operations.Contracts.FlightSubmissionUpdated
                {
                    FlightId = flight.Id, FlightNumber = flight.FlightNumber.Value, SubmissionId = id,
                    SubmissionKind = kind, Action = deleted ? global::Operations.Contracts.FlightSubmissionAction.Deleted : global::Operations.Contracts.FlightSubmissionAction.Updated,
                    SubmittedByStaffMemberId = submittedStaffId, SubmittedByUserId = creator,
                    UpdatedByStaffMemberId = actor.StaffMemberId, UpdatedByUserId = actor.UserId,
                    UpdatedByDisplayName = names.GetValueOrDefault(actor.UserId) ?? "", OccurredOnUtc = now
                });
            }
            if (savedArrival is not null && arrivalTarget is not null)
                Notify(savedArrival.Id, savedArrival.CreatedByUserId, global::Operations.Contracts.FlightSubmissionKind.Arrival, savedArrival.SubmittedByEmployee.StaffMemberId);
            if (savedDeparture is not null && departureTarget is not null)
                Notify(savedDeparture.Id, savedDeparture.CreatedByUserId, global::Operations.Contracts.FlightSubmissionKind.Departure, savedDeparture.SubmittedByEmployee.StaffMemberId);
            foreach (var edit in preparedServices.Where(item => !item.Matches && item.Existing is not null))
                Notify(edit.Change.RecordId, edit.Existing!.CreatedByUserId, global::Operations.Contracts.FlightSubmissionKind.Service, deleted: edit.Change.Input is null);
            foreach (var edit in preparedTasks.Where(item => !item.Matches && item.Existing is not null))
                Notify(edit.Change.RecordId, edit.Existing!.CreatedByUserId, global::Operations.Contracts.FlightSubmissionKind.Task, deleted: edit.Change.Input is null);
            db.SetOriginalRowVersion(flight, request.RowVersion);
            await FlightActivityRecorder.TouchAsync(db, flight, actor, clock.GetUtcNow(), ct);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return ConcurrencyErrors.Stale; }
            catch (DbUpdateException)
            {
                // A concurrent first actual can hit its unique index before the parent update.
                var persisted = await db.Flights.AsNoTracking().Where(item => item.Id == flight.Id).Select(item => item.RowVersion).SingleOrDefaultAsync(ct);
                if (persisted is not null && !persisted.SequenceEqual(request.RowVersion)) return ConcurrencyErrors.Stale;
                throw;
            }
            FlightServiceWrites.Notify(mobileSync, clock);
            return Response();
        }
        finally
        {
            // A commit may have succeeded despite a lost response. Persisted references decide cleanup.
            await FlightServiceFiles.DeleteUnreferencedAsync(db, storage, createdKeys.Concat(removedKeys));
        }
    }

    private static Result<FlightArrivalSubmission?> ArrivalTarget(Flight flight, SaveFlightArrivalRequest? input, MobileFlightScopeContext actor, bool assigned)
    {
        if (input is null) return Result.Success<FlightArrivalSubmission?>(null);
        if (input.TargetSubmissionId == Guid.Empty) return Invalid("Target arrival submission is required.");
        var target = input.TargetSubmissionId is { } id ? flight.ArrivalSubmissions.SingleOrDefault(item => item.Id == id)
            : flight.ArrivalSubmissions.SingleOrDefault(item => item.SubmittedByEmployee.StaffMemberId == actor.StaffMemberId);
        if (input.TargetSubmissionId.HasValue && target is null) return Error.NotFound("Arrival submission not found.", "Operations.ArrivalSubmission.NotFound");
        if (target is null && flight.ArrivalSubmissions.Count == 1) target = flight.ArrivalSubmissions.Single();
        if (target is null && flight.ArrivalSubmissions.Count > 1)
            return Error.Conflict("Select the arrival submission to update.", "Operations.ArrivalSubmission.TargetRequired");
        if (target is not null && target.SubmittedByEmployee.StaffMemberId != actor.StaffMemberId && !assigned)
            return Error.Forbidden("Only assigned employees can update another employee's arrival.", "Operations.ArrivalSubmission.AssignmentRequired");
        return Result.Success(target);
    }

    private static Result<FlightDepartureSubmission?> DepartureTarget(Flight flight, SaveFlightDepartureRequest? input, MobileFlightScopeContext actor, bool assigned)
    {
        if (input is null) return Result.Success<FlightDepartureSubmission?>(null);
        if (input.TargetSubmissionId == Guid.Empty) return Invalid("Target departure submission is required.");
        var target = input.TargetSubmissionId is { } id ? flight.DepartureSubmissions.SingleOrDefault(item => item.Id == id)
            : flight.DepartureSubmissions.SingleOrDefault(item => item.SubmittedByEmployee.StaffMemberId == actor.StaffMemberId);
        if (input.TargetSubmissionId.HasValue && target is null) return Error.NotFound("Departure submission not found.", "Operations.DepartureSubmission.NotFound");
        if (target is null && flight.DepartureSubmissions.Count == 1) target = flight.DepartureSubmissions.Single();
        if (target is null && flight.DepartureSubmissions.Count > 1)
            return Error.Conflict("Select the departure submission to update.", "Operations.DepartureSubmission.TargetRequired");
        if (target is not null && target.SubmittedByEmployee.StaffMemberId != actor.StaffMemberId && !assigned)
            return Error.Forbidden("Only assigned employees can update another employee's departure.", "Operations.DepartureSubmission.AssignmentRequired");
        return Result.Success(target);
    }

    private static bool ArrivalMatches(FlightArrivalSubmission? existing, SaveFlightArrivalRequest input, Guid staffId) => existing is not null &&
        existing.Details.Ata == input.Ata && existing.Details.AircraftType.AircraftTypeId == input.AircraftTypeId &&
        IdentifiersMatch(existing.Details.AircraftTailNumber, input.AircraftTailNumber) &&
        IdentifiersMatch(existing.Details.CustomerWorkReferenceNumber, input.CustomerWorkReferenceNumber) && existing.Details.Remarks == Normalize(input.Remarks) &&
        (existing.SubmittedByEmployee.StaffMemberId != staffId || existing.Details.ReceivedByEmployee.StaffMemberId == staffId);

    private static Result ValidateDeparture(SaveFlightDepartureRequest? input, FlightDepartureSubmission? existing)
    {
        if (input is null) return Result.Success();
        if (input.Atd == default) return Invalid("Actual departure time is required.");
        if (input.SignatureContent is { } bytes)
        {
            if (bytes.Length > 2 * 1024 * 1024 || !SaveFlightDepartureCommandHandler.IsPng(bytes))
                return Error.Validation("The customer signature must be a PNG image of at most 2 MB.", "Operations.Departure.SignatureInvalid");
            if (input.SignatureSignedAtUtc is null || input.SignatureSignedAtUtc == default(DateTimeOffset))
                return Error.Validation("Customer signing time is required.", "Operations.CustomerSignature.SignedAtRequired");
        }
        else if (input.SignatureSignedAtUtc is not null)
            return Error.Validation("Customer signature is required.", "Operations.DepartureDetails.CustomerSignatureRequired");
        return Result.Success();
    }

    private async Task<bool> DepartureMatchesAsync(FlightDepartureSubmission? existing, SaveFlightDepartureRequest input, Guid staffId, CancellationToken ct)
    {
        if (existing is null || existing.Details.Atd != input.Atd || existing.Details.Remarks != Normalize(input.Remarks) ||
            existing.SubmittedByEmployee.StaffMemberId == staffId && existing.Details.HandledByEmployee.StaffMemberId != staffId) return false;
        if (input.SignatureContent is null) return true;
        if (existing.Details.CustomerSignature is not { } signature || signature.SignedAtUtc != input.SignatureSignedAtUtc) return false;
        await using var stream = await storage.OpenAsync(signature.StorageKey, ct);
        if (stream is null) return false;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray().AsSpan().SequenceEqual(input.SignatureContent);
    }

    private static FlightDetailDto Map(Flight flight, string zone, MobileFlightScopeContext actor, bool assigned,
        IReadOnlyList<Guid> allowedIds, IReadOnlyDictionary<Guid, string> names)
    {
        var ownCancellation = flight.CancellationSubmissions.Any(item => item.SubmittedByEmployee.StaffMemberId == actor.StaffMemberId);
        var ownWork = flight.ArrivalSubmissions.Any(item => item.SubmittedByEmployee.StaffMemberId == actor.StaffMemberId) ||
            flight.DepartureSubmissions.Any(item => item.SubmittedByEmployee.StaffMemberId == actor.StaffMemberId);
        return FlightDetails.Map(flight, zone, actor.StaffMemberId, !ownCancellation, !ownCancellation,
            !ownWork && flight.ServiceRecords.Count == 0 && flight.TaskRecords.Count == 0,
            flight.CancellationSubmissions.Count == 0 && allowedIds.Count > 0, actor.UserId, allowedIds,
            flight.CancellationSubmissions.Count == 0, assigned, names, canEditFlightDetails: true);
    }

    private static bool IdentifiersMatch(string? stored, string? input) => Normalize(stored)?.ToUpperInvariant() == Normalize(input)?.ToUpperInvariant();
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string NormalizeMime(string? value) => (value ?? "").Trim().ToLowerInvariant() is "audio/x-m4a" ? "audio/mp4" : (value ?? "").Trim().ToLowerInvariant();
    private static Error Invalid(string message) => Error.Validation(message, "Operations.FlightBatch.RequestInvalid");
    private static Error ImmutableAttachment() => Error.Validation("Use a new attachment ID to replace a file.", "Operations.FlightService.AttachmentImmutable");

    private async Task<Result> ValidateIdentitiesAsync(Guid flightId, IReadOnlyList<ServiceEdit> services, IReadOnlyList<TaskEdit> tasks, CancellationToken ct)
    {
        static bool Repeated(IEnumerable<(Guid Child, Guid Parent)> values) => values.GroupBy(item => item.Child).Any(group => group.Select(item => item.Parent).Distinct().Count() > 1);
        var serviceSaves = services.Where(item => item.Change.Input is not null).ToArray();
        if (Repeated(serviceSaves.SelectMany(edit => edit.Change.Input!.Performers.Select(item => (item.Id, edit.Change.RecordId)))) ||
            Repeated(serviceSaves.SelectMany(edit => (edit.Change.Input!.Attachments ?? []).Select(item => (item.Id, edit.Change.RecordId)))))
            return Error.Conflict("Service child record IDs cannot be shared between records.", "Operations.FlightService.IdConflict");
        foreach (var edit in serviceSaves)
        {
            var performers = edit.Change.Input!.Performers.Select(item => item.Id).ToArray();
            var attachments = edit.Change.Input.Attachments?.Select(item => item.Id).ToArray() ?? [];
            if (await db.Flights.AsNoTracking().AnyAsync(f => f.Id != flightId && f.ServiceRecords.Any(item => item.Id == edit.Change.RecordId) ||
                f.ServiceRecords.Any(item => (f.Id != flightId || item.Id != edit.Change.RecordId) &&
                    (item.Performers.Any(child => performers.Contains(child.Id)) || item.Attachments.Any(child => attachments.Contains(child.Id)))), ct))
                return Error.Conflict("A service or child record ID is already in use.", "Operations.FlightService.IdConflict");
        }
        var taskSaves = tasks.Where(item => item.Change.Input is not null).ToArray();
        if (Repeated(taskSaves.SelectMany(edit => edit.Change.Input!.Performers.Select(item => (item.Id, edit.Change.RecordId)))) ||
            Repeated(taskSaves.SelectMany(edit => edit.Change.Input!.Tools.Select(item => (item.Id, edit.Change.RecordId)))) ||
            Repeated(taskSaves.SelectMany(edit => edit.Change.Input!.Materials.Select(item => (item.Id, edit.Change.RecordId)))) ||
            Repeated(taskSaves.SelectMany(edit => edit.Change.Input!.GeneralSupports.Select(item => (item.Id, edit.Change.RecordId)))) ||
            Repeated(taskSaves.SelectMany(edit => (edit.Change.Input!.Attachments ?? []).Select(item => (item.Id, edit.Change.RecordId)))))
            return Error.Conflict("Task child record IDs cannot be shared between records.", "Operations.FlightTask.IdConflict");
        foreach (var edit in taskSaves)
        {
            var value = edit.Change.Input!;
            var performers = value.Performers.Select(item => item.Id).ToArray();
            var tools = value.Tools.Select(item => item.Id).ToArray();
            var materials = value.Materials.Select(item => item.Id).ToArray();
            var supports = value.GeneralSupports.Select(item => item.Id).ToArray();
            var attachments = value.Attachments?.Select(item => item.Id).ToArray() ?? [];
            if (await db.Flights.AsNoTracking().AnyAsync(f => f.Id != flightId && f.TaskRecords.Any(item => item.Id == edit.Change.RecordId) ||
                f.TaskRecords.Any(item => (f.Id != flightId || item.Id != edit.Change.RecordId) &&
                    (item.Performers.Any(child => performers.Contains(child.Id)) || item.Tools.Any(child => tools.Contains(child.Id)) ||
                     item.Materials.Any(child => materials.Contains(child.Id)) || item.GeneralSupports.Any(child => supports.Contains(child.Id)) ||
                     item.Attachments.Any(child => attachments.Contains(child.Id)))), ct))
                return Error.Conflict("A task or child record ID is already in use.", "Operations.FlightTask.IdConflict");
        }
        return Result.Success();
    }

    private async Task<Result> ApplyServiceFilesAsync(Flight flight, Guid recordId, IReadOnlyList<SaveFlightServiceAttachmentRequest> desired,
        Guid actor, DateTimeOffset now, List<string> created, List<string> removed, CancellationToken ct)
    {
        var record = flight.Services.Single(item => item.Id == recordId);
        foreach (var old in record.Attachments.Where(item => desired.All(input => input.Id != item.Id)).ToArray())
        {
            removed.Add(old.Details.StorageKey);
            var deleted = flight.RemoveServiceAttachment(recordId, old.Id, actor, now);
            if (deleted.IsFailure) return deleted;
        }
        foreach (var input in desired.Where(item => record.Attachments.All(old => old.Id != item.Id)))
        {
            using var content = new MemoryStream(input.Content!, writable: false);
            var stored = await storage.SaveAsync("flight-service-attachments", NormalizeFileName(input.FileName), input.ContentType.Trim(), content, ct);
            created.Add(stored.StorageKey);
            var details = AttachmentDetails.Create(input.FileName, stored.StorageKey, stored.ContentType, stored.SizeBytes, (AttachmentKind)input.Kind);
            if (details.IsFailure) return details.Error;
            var added = flight.AddServiceAttachment(recordId, details.Value, actor, now, id: input.Id);
            if (added.IsFailure) return added.Error;
        }
        return Result.Success();
    }

    private async Task<Result> ApplyTaskFilesAsync(Flight flight, Guid recordId, IReadOnlyList<SaveFlightServiceAttachmentRequest> desired,
        Guid actor, DateTimeOffset now, List<string> created, List<string> removed, CancellationToken ct)
    {
        var record = flight.Tasks.Single(item => item.Id == recordId);
        foreach (var old in record.Attachments.Where(item => desired.All(input => input.Id != item.Id)).ToArray())
        {
            removed.Add(old.Details.StorageKey);
            var deleted = flight.RemoveTaskAttachment(recordId, old.Id, actor, now);
            if (deleted.IsFailure) return deleted;
        }
        foreach (var input in desired.Where(item => record.Attachments.All(old => old.Id != item.Id)))
        {
            using var content = new MemoryStream(input.Content!, writable: false);
            var stored = await storage.SaveAsync("flight-task-attachments", NormalizeFileName(input.FileName), input.ContentType.Trim(), content, ct);
            created.Add(stored.StorageKey);
            var details = AttachmentDetails.Create(input.FileName, stored.StorageKey, stored.ContentType, stored.SizeBytes, (AttachmentKind)input.Kind);
            if (details.IsFailure) return details.Error;
            var added = flight.AddTaskAttachment(recordId, details.Value, actor, now, id: input.Id);
            if (added.IsFailure) return added.Error;
        }
        return Result.Success();
    }

    private static string NormalizeFileName(string value)
    {
        var name = Path.GetFileName(value.Trim().Replace('\\', '/'));
        return name.Length <= 255 ? name : name[..255];
    }
}
