using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Results;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

/// <summary>A single return to ramp and the work belonging to that occurrence.</summary>
public sealed class FlightReturnToRamp : Entity<Guid>
{
    private readonly List<FlightService> _services = [];
    private readonly List<FlightTask> _tasks = [];

    private FlightReturnToRamp() { }

    internal FlightReturnToRamp(Guid id, Guid flightId, int sequence, RtrArrivalDetails arrivalDetails,
        Guid userId, DateTimeOffset now)
    {
        Id = id;
        FlightId = flightId;
        Sequence = sequence;
        ArrivalDetails = arrivalDetails.Copy();
        CreatedByUserId = userId;
        CreatedAtUtc = now;
    }

    public Guid FlightId { get; private set; }
    public int Sequence { get; private set; }
    public RtrArrivalDetails ArrivalDetails { get; private set; } = null!;
    public RtrDepartureDetails? DepartureDetails { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? EndedByUserId { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public bool IsOpen => DepartureDetails is null;
    public TimeWindow Window => TimeWindow.Create(ArrivalDetails.ArrivedAtUtc, DepartureDetails?.DepartedAtUtc).Value;
    public IReadOnlyList<FlightService> Services => _services.AsReadOnly();
    public IReadOnlyList<FlightTask> Tasks => _tasks.AsReadOnly();

    internal Result<bool> UpdateArrival(RtrArrivalDetails details)
    {
        if (ArrivalDetails == details)
            return false;
        var check = ValidateActivities(details.ArrivedAtUtc, DepartureDetails?.DepartedAtUtc);
        if (check.IsFailure)
            return check.Error;
        ArrivalDetails = details.Copy();
        return true;
    }

    internal Result<bool> SetDeparture(RtrDepartureDetails details, Guid userId)
    {
        if (DepartureDetails == details)
            return false;
        var check = ValidateActivities(ArrivalDetails.ArrivedAtUtc, details.DepartedAtUtc);
        if (check.IsFailure)
            return check.Error;
        if (DepartureDetails?.DepartedAtUtc != details.DepartedAtUtc)
            EndedByUserId = userId;
        DepartureDetails = details.Copy();
        return true;
    }

    internal Result ValidateService(TimeWindow? window, IEnumerable<TimeWindow?> employees) =>
        ValidateWork(window, employees, "Service", ArrivalDetails.ArrivedAtUtc, DepartureDetails?.DepartedAtUtc);

    internal Result ValidateTask(TimeWindow? window, IEnumerable<TimeWindow?> employees) =>
        ValidateWork(window, employees, "Task", ArrivalDetails.ArrivedAtUtc, DepartureDetails?.DepartedAtUtc);

    internal Result ValidateEmployeeWindow(TimeWindow? window) =>
        !IsOpen && window is not null && window.ToUtc is null
            ? Error.Validation("A departed RTR requires closed employee periods.", "Operations.ReturnToRamp.EmployeeWindowRequired")
            : Result.Success();

    internal Result ValidatePerformerRemoval(int count, string kind) =>
        !IsOpen && count <= 1 ? PerformerRequired(kind) : Result.Success();

    internal Result ValidateActivityRemoval() =>
        !IsOpen && _services.Count + _tasks.Count <= 1
            ? Error.Validation("Return to ramp requires at least one service or task.", "Operations.ReturnToRamp.ActivityRequired")
            : Result.Success();

    internal void AddService(FlightService service) => _services.Add(service);
    internal void AddTask(FlightTask task) => _tasks.Add(task);
    internal void RemoveService(FlightService service) => _services.Remove(service);
    internal void RemoveTask(FlightTask task) => _tasks.Remove(task);

    internal void MarkUpdated(Guid userId, DateTimeOffset now)
    {
        UpdatedByUserId = userId;
        UpdatedAtUtc = now;
    }

    private Result ValidateActivities(DateTimeOffset arrival, DateTimeOffset? departure)
    {
        if (departure < arrival)
            return Error.Validation("RTR departure cannot be before its arrival.", "Operations.ReturnToRamp.WindowInvalid");
        if (departure.HasValue && _services.Count + _tasks.Count == 0)
            return Error.Validation("Return to ramp requires at least one service or task.", "Operations.ReturnToRamp.ActivityRequired");
        foreach (var service in _services)
        {
            var check = ValidateWork(service.Window, service.Performers.Select(item => item.Window), "Service", arrival, departure);
            if (check.IsFailure)
                return check;
        }
        foreach (var task in _tasks)
        {
            var check = ValidateWork(task.Window, task.Performers.Select(item => item.Window), "Task", arrival, departure);
            if (check.IsFailure)
                return check;
        }
        return Result.Success();
    }

    private static Result ValidateWork(TimeWindow? window, IEnumerable<TimeWindow?> employees, string kind,
        DateTimeOffset arrival, DateTimeOffset? departure)
    {
        // The shared activity factory reports its established missing-window validation.
        if (window is null)
            return Result.Success();
        if (window.FromUtc < arrival || (departure.HasValue && (window.FromUtc > departure || window.ToUtc > departure)))
            return Error.Validation("RTR work times must be inside the occurrence window.",
                $"Operations.ReturnToRamp.{kind}WindowOutsideOccurrence");
        if (!departure.HasValue)
            return Result.Success();
        if (window.ToUtc is null)
            return Error.Validation("A departed RTR requires closed service and task periods.", "Operations.ReturnToRamp.ActivityWindowRequired");
        var periods = employees.ToList();
        if (periods.Count == 0)
            return PerformerRequired(kind);
        if (periods.Any(period => period is not null && period.ToUtc is null))
            return Error.Validation("A departed RTR requires closed employee periods.", "Operations.ReturnToRamp.EmployeeWindowRequired");
        return Result.Success();
    }

    private static Error PerformerRequired(string kind) =>
        kind == "Service"
            ? Error.Validation("Every RTR service requires at least one performer.", "Operations.ReturnToRamp.ServiceLinePerformerRequired")
            : Error.Validation("Every RTR task requires at least one employee.", "Operations.ReturnToRamp.TaskEmployeeRequired");
}
