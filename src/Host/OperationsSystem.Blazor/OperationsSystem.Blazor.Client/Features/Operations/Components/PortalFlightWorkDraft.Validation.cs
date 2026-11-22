using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

public sealed partial class PortalWorkDraft
{
    internal static void ValidateServicePeriods(IReadOnlyList<PortalWorkDraft> services, PortalFlightValidation errors,
        bool includeUnchanged = false)
    {
        var serviceGroups = services.Select((row, index) => (Row: row, Index: index + 1))
            .Where(item => !item.Row.Deleted && item.Row.ServiceId.HasValue && item.Row.ServiceId != Guid.Empty)
            .Select(item => (item.Row, item.Index, Period: ReadPeriod(item.Row.Start, item.Row.End)))
            .Where(item => item.Period.HasValue)
            .GroupBy(item => item.Row.ServiceId);

        foreach (var group in serviceGroups)
        {
            var periods = group.Select(item => (item.Row, item.Index, Period: item.Period!.Value,
                Validate: includeUnchanged || item.Row.HasChanges())).ToArray();
            for (var first = 0; first < periods.Length; first++)
            for (var second = first + 1; second < periods.Length; second++)
            {
                var left = periods[first];
                var right = periods[second];
                if (!(left.Validate || right.Validate) || !left.Period.ConflictsWith(right.Period))
                    continue;

                if (left.Validate)
                    errors.Add(left.Row.Field("start"), $"Service {left.Index} overlaps Service {right.Index} for the same service.");
                if (right.Validate)
                    errors.Add(right.Row.Field("start"), $"Service {right.Index} overlaps Service {left.Index} for the same service.");
            }
        }
    }

    internal static void ValidateDeparturePeriods(IReadOnlyList<PortalWorkDraft> services, IReadOnlyList<PortalWorkDraft> tasks,
        PortalFlightValidation errors, string departureTimeField)
    {
        Check(services, "Service");
        Check(tasks, "Task");

        void Check(IReadOnlyList<PortalWorkDraft> rows, string kind)
        {
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                if (row.Deleted || row.Start.Local is null)
                    continue;

                if (ReadPeriod(row.Start, row.End) is not { End: not null } || row.Performers.Count == 0 ||
                    row.Performers.Any(person => ReadPeriod(person.Start, person.End) is not { End: not null }) ||
                    row.DurationResources().Any(resource => ReadPeriod(resource.Start, resource.End) is not { End: not null }))
                    errors.Add(departureTimeField, $"Complete {kind} {index + 1} and all its work periods before entering departure.");
            }
        }
    }

    internal static void ValidateActualBounds(IReadOnlyList<PortalWorkDraft> work, PortalFlightValidation errors,
        DateTimeOffset? actualArrival, DateTimeOffset? actualDeparture, bool includeUnchanged = false)
    {
        for (var index = 0; index < work.Count; index++)
        {
            var row = work[index];
            if (row.Deleted || row.Start.Local is null || !includeUnchanged && !row.HasChanges())
                continue;

            var label = $"{(row.IsTask ? "Task" : "Service")} {index + 1}";
            Check(row.Id, label, row.Start, row.End);
            for (var personIndex = 0; personIndex < row.Performers.Count; personIndex++)
            {
                var person = row.Performers[personIndex];
                Check(person.Id, $"{label} performer {personIndex + 1}", person.Start, person.End);
            }

            foreach (var resource in row.DurationResources())
                Check(resource.Id, $"{label} {resource.Name}", resource.Start, resource.End);
        }

        void Check(Guid id, string label, PortalOperationTime start, PortalOperationTime end)
        {
            if (start.TryReadUtc(out var from, out _) && from.HasValue)
            {
                if (from < actualArrival)
                    errors.Add(PortalFlightValidation.Field(id, "start"), $"{label} start cannot be before actual arrival.");
                if (from > actualDeparture)
                    errors.Add(PortalFlightValidation.Field(id, "start"), $"{label} start cannot be after actual departure.");
            }

            if (end.TryReadUtc(out var to, out _) && to.HasValue && to > actualDeparture)
                errors.Add(PortalFlightValidation.Field(id, "end"), $"{label} end cannot be after actual departure.");
        }
    }

    private IEnumerable<PortalResourceDraft> DurationResources() =>
        Tools.Concat(Materials).Concat(GeneralSupports).Where(item => item.CalculationType is ResourceCalculationType.Duration);

    private static void ValidateContainedPeriod(Guid id, string label, DateTimeOffset? start, DateTimeOffset? end,
        string parentLabel, DateTimeOffset? parentStart, DateTimeOffset? parentEnd, PortalFlightValidation errors)
    {
        if (start < parentStart)
            errors.Add(PortalFlightValidation.Field(id, "start"), $"{label} start cannot be before the start of {parentLabel}.");
        if (start > parentEnd)
            errors.Add(PortalFlightValidation.Field(id, "start"), $"{label} start cannot be after the end of {parentLabel}.");
        if (end > parentEnd)
            errors.Add(PortalFlightValidation.Field(id, "end"), $"{label} end cannot be after the end of {parentLabel}.");
    }

    private static void ValidatePerformerPeriods(IReadOnlyList<PortalFlightPerformerRequest> performers, string label,
        PortalFlightValidation errors)
    {
        var employeeGroups = performers.Select((input, index) => (Input: input, Index: index + 1))
            .Where(item => item.Input.StaffMemberId != Guid.Empty && item.Input.StartUtc != default &&
                !errors.HasError(PortalFlightValidation.Field(item.Input.Id, "start")) &&
                !errors.HasError(PortalFlightValidation.Field(item.Input.Id, "end")))
            .GroupBy(item => item.Input.StaffMemberId);

        foreach (var employee in employeeGroups)
        {
            var periods = employee.Select(item => (item.Input.Id, item.Index,
                Period: new WorkPeriod(item.Input.StartUtc, item.Input.EndUtc))).ToArray();
            for (var first = 0; first < periods.Length; first++)
            for (var second = first + 1; second < periods.Length; second++)
            {
                var left = periods[first];
                var right = periods[second];
                if (left.Period.ConflictsWith(right.Period))
                    errors.Add(PortalFlightValidation.Field(right.Id, "start"),
                        $"{label} performer {right.Index} overlaps performer {left.Index} for the same employee.");
            }
        }
    }

    private static WorkPeriod? ReadPeriod(PortalOperationTime start, PortalOperationTime end)
    {
        if (!start.TryReadUtc(out var from, out _) || from is not { } utcStart || utcStart == default ||
            !end.TryReadUtc(out var to, out _) || to < utcStart)
            return null;

        return new(utcStart, to);
    }

    // The domain allows adjacent periods and distinct zero-duration periods, but never identical periods.
    private readonly record struct WorkPeriod(DateTimeOffset Start, DateTimeOffset? End)
    {
        public bool ConflictsWith(WorkPeriod other) => this == other ||
            End != Start && other.End != other.Start &&
            (!other.End.HasValue || Start < other.End.Value) && (!End.HasValue || other.Start < End.Value);
    }
}
