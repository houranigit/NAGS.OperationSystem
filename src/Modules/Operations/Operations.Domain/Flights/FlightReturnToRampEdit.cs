using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

public sealed record ReturnToRampServiceInput(Guid Id, ServiceSnapshot Service, string? CustomerReferenceNumber,
    TimeWindow Window, string? Notes, IReadOnlyList<FlightServicePerformerInput> Performers);
public sealed record ReturnToRampTaskInput(Guid Id, FlightTaskType TaskType, AtaChapterSnapshot? AtaChapter,
    string? CustomerReferenceNumber, TimeWindow Window, string? Notes, IReadOnlyList<FlightTaskPerformerInput> Performers,
    IReadOnlyList<FlightTaskToolInput> Tools, IReadOnlyList<FlightTaskMaterialInput> Materials,
    IReadOnlyList<FlightTaskGeneralSupportInput> GeneralSupports);

public sealed partial class Flight
{
    /// <summary>Validate the complete occurrence before reconciling stable records and their original attribution.</summary>
    public Result<FlightReturnToRamp> SaveReturnToRamp(Guid id, RtrArrivalDetails arrival, RtrDepartureDetails? departure,
        IReadOnlyList<ReturnToRampServiceInput> services, IReadOnlyList<ReturnToRampTaskInput> tasks,
        Guid userId, DateTimeOffset now)
    {
        var check = ValidateReturnToRampWrite(userId);
        if (check.IsFailure) return check.Error;
        if (id == Guid.Empty || arrival is null || services is null || tasks is null)
            return Error.Validation("An identified return to ramp and its arrival are required.", "Operations.ReturnToRamp.RequestInvalid");
        var existing = _returnToRamps.SingleOrDefault(r => r.Id == id);
        if (_departureSubmissions.Count == 0)
            return Error.Conflict("Record departure before adding return to ramp.", "Operations.ReturnToRamp.DepartureRequired");
        if (existing is null && _returnToRamps.Any(r => r.IsOpen) || departure is null &&
            _returnToRamps.Any(r => r.Id != id && (r.IsOpen || existing is not null && r.Sequence > existing.Sequence)))
            return Error.Conflict("Close the previous return to ramp first.", "Operations.ReturnToRamp.OpenExists");
        if (existing is null && LastReturnToRampSequence == int.MaxValue)
            return Error.Conflict("The RTR sequence limit has been reached.", "Operations.ReturnToRamp.SequenceExhausted");
        if (services.Select(s => s.Id).Distinct().Count() != services.Count || tasks.Select(t => t.Id).Distinct().Count() != tasks.Count ||
            services.Any(s => s.Id == Guid.Empty || _services.Any(old => old.Id == s.Id && old.ReturnToRampId != id)) ||
            tasks.Any(t => t.Id == Guid.Empty || _tasks.Any(old => old.Id == t.Id && old.ReturnToRampId != id)))
            return Error.Conflict("A work record identity is already in use.", "Operations.ReturnToRamp.WorkIdConflict");
        var candidate = new FlightReturnToRamp(id, Id, existing?.Sequence ?? LastReturnToRampSequence + 1, arrival, userId, now);
        foreach (var input in services)
        {
            var result = FlightService.Create(Id, input.Service, input.CustomerReferenceNumber, input.Window, input.Notes, userId, now, id, input.Id);
            if (result.IsFailure) return result.Error;
            foreach (var performer in input.Performers)
            {
                var added = result.Value.AddPerformer(performer.Employee, performer.Window, performer.Id);
                if (added.IsFailure) return added.Error;
            }
            var bounds = candidate.ValidateService(result.Value.Window, result.Value.Performers.Select(p => p.Window));
            if (bounds.IsFailure) return bounds.Error;
            candidate.AddService(result.Value);
        }
        foreach (var input in tasks)
        {
            var result = FlightTask.Create(Id, input.TaskType, input.AtaChapter, input.CustomerReferenceNumber, input.Window, input.Notes, userId, now, id, input.Id);
            if (result.IsFailure) return result.Error;
            foreach (var person in input.Performers) { var added = result.Value.AddPerformer(person.Employee, person.Window, person.Notes, person.Id); if (added.IsFailure) return added.Error; }
            foreach (var tool in input.Tools) { var added = result.Value.AddTool(tool.Tool, tool.Usage, tool.Notes, tool.Id); if (added.IsFailure) return added.Error; }
            foreach (var material in input.Materials) { var added = result.Value.AddMaterial(material.Material, material.Usage, material.Notes, material.Id); if (added.IsFailure) return added.Error; }
            foreach (var support in input.GeneralSupports) { var added = result.Value.AddGeneralSupport(support.GeneralSupport, support.Usage, support.Notes, support.Id); if (added.IsFailure) return added.Error; }
            var bounds = candidate.ValidateTask(result.Value.Window, result.Value.Performers.Select(p => p.Window));
            if (bounds.IsFailure) return bounds.Error;
            if (departure is not null && result.Value.Tools.Select(t => t.Usage).Concat(result.Value.Materials.Select(t => t.Usage)).Concat(result.Value.GeneralSupports.Select(t => t.Usage)).Any(u => u.Quantity is null && u.ToUtc is null))
                return Error.Validation("Close duration resources before RTR departure.", "Operations.ReturnToRamp.ResourceWindowRequired");
            candidate.AddTask(result.Value);
        }
        static bool ChildConflict(IEnumerable<(Guid Parent, Guid Child)> desired, IEnumerable<(Guid Parent, Guid Child)> recorded)
        {
            var rows = desired.ToArray();
            var parents = recorded.ToDictionary(row => row.Child, row => row.Parent);
            return rows.Select(row => row.Child).Distinct().Count() != rows.Length ||
                rows.Any(row => parents.TryGetValue(row.Child, out var owner) && owner != row.Parent);
        }
        if (ChildConflict(candidate.Services.SelectMany(s => s.Performers.Select(p => (s.Id, p.Id))), _services.SelectMany(s => s.Performers.Select(p => (s.Id, p.Id)))) ||
            ChildConflict(candidate.Tasks.SelectMany(t => t.Performers.Select(p => (t.Id, p.Id))), _tasks.SelectMany(t => t.Performers.Select(p => (t.Id, p.Id)))) ||
            ChildConflict(candidate.Tasks.SelectMany(t => t.Tools.Select(p => (t.Id, p.Id))), _tasks.SelectMany(t => t.Tools.Select(p => (t.Id, p.Id)))) ||
            ChildConflict(candidate.Tasks.SelectMany(t => t.Materials.Select(p => (t.Id, p.Id))), _tasks.SelectMany(t => t.Materials.Select(p => (t.Id, p.Id)))) ||
            ChildConflict(candidate.Tasks.SelectMany(t => t.GeneralSupports.Select(p => (t.Id, p.Id))), _tasks.SelectMany(t => t.GeneralSupports.Select(p => (t.Id, p.Id)))))
            return Error.Conflict("A work child identity belongs to another record.", "Operations.ReturnToRamp.ChildIdConflict");
        foreach (var group in candidate.Services.Where(s => s.Service.ServiceId != WellKnownMasterDataIds.UnknownService).GroupBy(s => s.Service.ServiceId))
        {
            var items = group.ToArray();
            for (var first = 0; first < items.Length; first++) for (var second = first + 1; second < items.Length; second++)
            {
                var a = items[first].Window; var b = items[second].Window;
                if (a == b || a.ToUtc != a.FromUtc && b.ToUtc != b.FromUtc &&
                    (!b.ToUtc.HasValue || a.FromUtc < b.ToUtc) && (!a.ToUtc.HasValue || b.FromUtc < a.ToUtc))
                    return Error.Validation("Occurrences of the same service cannot overlap.", "Operations.FlightService.OverlappingPeriods");
            }
        }
        if (departure is not null) { var closed = candidate.SetDeparture(departure, userId); if (closed.IsFailure) return closed.Error; }
        var occurrence = existing ?? candidate;
        if (existing is null)
        {
            _returnToRamps.Add(candidate); LastReturnToRampSequence = candidate.Sequence;
            _services.AddRange(candidate.Services); _tasks.AddRange(candidate.Tasks);
        }
        else
        {
            foreach (var old in _services.Where(s => s.ReturnToRampId == id && candidate.Services.All(n => n.Id != s.Id)).ToArray()) { _services.Remove(old); existing.RemoveService(old); }
            foreach (var old in _tasks.Where(t => t.ReturnToRampId == id && candidate.Tasks.All(n => n.Id != t.Id)).ToArray()) { _tasks.Remove(old); existing.RemoveTask(old); }
            foreach (var desired in candidate.Services)
            {
                var old = _services.SingleOrDefault(s => s.Id == desired.Id);
                if (old is null) { _services.Add(desired); existing.AddService(desired); }
                else if (old.ReplaceDetailsAndPerformers(desired, userId)) old.MarkUpdated(userId, now);
            }
            foreach (var desired in candidate.Tasks)
            {
                var old = _tasks.SingleOrDefault(t => t.Id == desired.Id);
                if (old is null) { _tasks.Add(desired); existing.AddTask(desired); }
                else if (old.ReplaceDetailsAndChildren(desired, userId)) old.MarkUpdated(userId, now);
            }
            existing.ReplaceValidatedDetails(arrival, departure, userId);
            existing.MarkUpdated(userId, now);
        }
        UpdatedAtUtc = now;
        return occurrence;
    }
}
