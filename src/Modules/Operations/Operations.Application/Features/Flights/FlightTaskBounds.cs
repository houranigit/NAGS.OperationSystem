using BuildingBlocks.Domain.Results;
using Operations.Application.Contracts;
using Operations.Domain.Flights;

namespace Operations.Application.Features.Flights;

internal static class FlightTaskBounds
{
    internal static Result Validate(Flight flight, SaveFlightTaskRequest input, Guid staffId)
    {
        var actuals = VisibleActuals(flight, staffId);
        var failures = new Dictionary<string, string[]>();
        CheckInput(input, actuals.Arrival, actuals.Departure, failures);
        return ResultFor(failures);
    }

    internal static Result ValidateExistingTasks(Flight flight, Guid staffId, DateTimeOffset? arrivalOverride = null,
        DateTimeOffset? departureOverride = null)
    {
        var actuals = VisibleActuals(flight, staffId);
        var failures = new Dictionary<string, string[]>();
        foreach (var task in flight.Tasks)
            CheckInput(Input(task), arrivalOverride ?? actuals.Arrival, departureOverride ?? actuals.Departure, failures, $"Tasks[{task.Id}].");
        return ResultFor(failures);
    }

    internal static Result RequireClosedPeriods(SaveFlightTaskRequest input)
    {
        if (input.EndUtc is null) return Result.Success();
        var failures = new Dictionary<string, string[]>();
        for (var i = 0; i < input.Performers.Count; i++)
            if (input.Performers[i].EndUtc is null)
                failures[$"Performers[{i}].EndUtc"] = ["Enter the employee end before closing the task."];
        var resourceOpen = false;
        void Resources(IReadOnlyList<SaveFlightTaskResourceRequest> rows, string name)
        {
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Quantity is null && rows[i].EndUtc is null)
                {
                    failures[$"{name}[{i}].EndUtc"] = ["Enter the duration resource end before closing the task."];
                    resourceOpen = true;
                }
        }
        Resources(input.Tools, "Tools"); Resources(input.Materials, "Materials"); Resources(input.GeneralSupports, "GeneralSupports");
        if (failures.Count == 0) return Result.Success();
        failures["EndUtc"] = ["Close all employee and duration resource periods before closing the task."];
        return Error.Validation(failures, "Close all employee and duration resource periods before closing the task.",
            resourceOpen ? "Operations.FlightTask.OpenResourcePeriods" : "Operations.FlightTask.OpenPerformerPeriods");
    }

    internal static Result RequireCompletedIfDeparted(Flight flight, SaveFlightTaskRequest input, Guid staffId) =>
        VisibleActuals(flight, staffId).Departure is null ? Result.Success() : Completion(input);

    internal static Result RequireCompletedForDeparture(Flight flight)
    {
        var failures = new Dictionary<string, string[]>();
        foreach (var task in flight.Tasks) AddCompletion(Input(task), $"Tasks[{task.Id}].", failures);
        return failures.Count == 0 ? Result.Success() : Error.Validation(failures,
            "Complete the end times for started tasks, employees and duration resources before saving departure.", "Operations.Departure.TaskWorkIncomplete");
    }

    private static Result Completion(SaveFlightTaskRequest input)
    {
        var failures = new Dictionary<string, string[]>();
        AddCompletion(input, "", failures);
        return failures.Count == 0 ? Result.Success() : Error.Validation(failures,
            "Task, employee and duration resource end times are required when departure is already recorded.", "Operations.FlightTask.EndRequiredAfterDeparture");
    }

    private static void AddCompletion(SaveFlightTaskRequest input, string prefix, Dictionary<string, string[]> failures)
    {
        if (input.EndUtc is null) failures[prefix + "EndUtc"] = ["Enter the task end before departure."];
        if (input.Performers.Count == 0) failures[prefix + "Performers"] = ["Every started task needs at least one employee."];
        for (var i = 0; i < input.Performers.Count; i++)
            if (input.Performers[i].EndUtc is null) failures[$"{prefix}Performers[{i}].EndUtc"] = ["Enter every employee's end before departure."];
        void Resources(IReadOnlyList<SaveFlightTaskResourceRequest> rows, string name)
        {
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Quantity is null && rows[i].EndUtc is null)
                    failures[$"{prefix}{name}[{i}].EndUtc"] = ["Enter every duration resource end before departure."];
        }
        Resources(input.Tools, "Tools"); Resources(input.Materials, "Materials"); Resources(input.GeneralSupports, "GeneralSupports");
    }

    internal static SaveFlightTaskRequest Input(FlightTask task) => new((int)task.TaskType, task.AtaChapter?.AtaChapterId,
        task.Window.FromUtc, task.Window.ToUtc, task.CustomerReferenceNumber, task.Notes,
        task.Performers.Select(p => new SaveFlightTaskPerformerRequest(p.Id, p.Employee.StaffMemberId, p.Window.FromUtc, p.Window.ToUtc, p.Notes)).ToList(),
        task.Tools.Select(item => new SaveFlightTaskResourceRequest(item.Id, item.Tool.ToolId, item.Usage.Quantity, item.Usage.FromUtc, item.Usage.ToUtc, item.Notes)).ToList(),
        task.Materials.Select(item => new SaveFlightTaskResourceRequest(item.Id, item.Material.MaterialId, item.Usage.Quantity, item.Usage.FromUtc, item.Usage.ToUtc, item.Notes)).ToList(),
        task.GeneralSupports.Select(item => new SaveFlightTaskResourceRequest(item.Id, item.GeneralSupport.GeneralSupportId, item.Usage.Quantity, item.Usage.FromUtc, item.Usage.ToUtc, item.Notes)).ToList());

    private static void CheckInput(SaveFlightTaskRequest input, DateTimeOffset? ata, DateTimeOffset? atd,
        Dictionary<string, string[]> failures, string prefix = "")
    {
        void Check(DateTimeOffset? start, DateTimeOffset? end, string fieldPrefix, string label)
        {
            if (start is null) return;
            if (ata.HasValue && start < ata) failures[fieldPrefix + "StartUtc"] = [$"{label} start cannot be before ATA."];
            if (atd.HasValue && start > atd) failures[fieldPrefix + "StartUtc"] = [$"{label} start cannot be after ATD."];
            if (ata.HasValue && end < ata) failures[fieldPrefix + "EndUtc"] = [$"{label} end cannot be before ATA."];
            if (atd.HasValue && end > atd) failures[fieldPrefix + "EndUtc"] = [$"{label} end cannot be after ATD."];
        }
        Check(input.StartUtc, input.EndUtc, prefix, "Task");
        for (var i = 0; i < input.Performers.Count; i++) Check(input.Performers[i].StartUtc, input.Performers[i].EndUtc, $"{prefix}Performers[{i}].", "Employee");
        // Resources use their task window; the separate completion rule requires
        // duration Ends when the task closes.
    }

    private static (DateTimeOffset? Arrival, DateTimeOffset? Departure) VisibleActuals(Flight flight, Guid staffId)
    {
        var arrivals = flight.ArrivalSubmissions.OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id).ToArray();
        var departures = flight.DepartureSubmissions.OrderBy(d => d.CreatedAtUtc).ThenBy(d => d.Id).ToArray();
        var arrival = arrivals.FirstOrDefault(a => a.SubmittedByEmployee.StaffMemberId == staffId) ?? arrivals.FirstOrDefault(a => a.IsAccepted) ?? arrivals.FirstOrDefault();
        var departure = departures.FirstOrDefault(d => d.SubmittedByEmployee.StaffMemberId == staffId) ?? departures.FirstOrDefault(d => d.IsAccepted) ?? departures.FirstOrDefault();
        return (arrival?.Details.Ata, departure?.Details.Atd);
    }

    private static Result ResultFor(Dictionary<string, string[]> failures) => failures.Count == 0 ? Result.Success() :
        Error.Validation(failures, "Task and employee periods must stay within the actual flight times.", "Operations.FlightTask.Validation");
}
