using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Auditing;
using BuildingBlocks.Domain.Results;
using Operations.Domain.Enumerations;
using Operations.Domain.Events;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

/// <summary>
/// A serviced aircraft event. The aggregate owns scheduling, planned services, assigned employees,
/// and flight-number history. Completion and cancellation will be reintroduced later with the next
/// lifecycle slice.
/// </summary>
public sealed class Flight : AggregateRoot<Guid>, IAuditable
{
    private readonly List<FlightAssignedEmployee> _assignedEmployees = [];
    private readonly List<PlannedService> _plannedServices = [];

    private Flight() { }

    string IAuditable.AuditEntityType => "Flight";
    Guid IAuditable.AuditEntityId => Id;

    public CustomerSnapshot Customer { get; private set; } = null!;
    public StationSnapshot Station { get; private set; } = null!;
    public OperationTypeSnapshot OperationType { get; private set; } = null!;
    public FlightNumber FlightNumber { get; private set; } = null!;

    /// <summary>The flight number captured at creation, preserved even when the current number changes.</summary>
    public string OriginalFlightNumber { get; private set; } = null!;

    public ScheduledTime Schedule { get; private set; } = null!;
    public AircraftTypeSnapshot? AircraftType { get; private set; }
    public FlightStatus Status { get; private set; }

    // Contract seam (no contract logic yet).
    public Guid? ContractId { get; private set; }
    public string? ContractNumber { get; private set; }

    // Merge metadata.
    public Guid? MergedIntoFlightId { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<FlightAssignedEmployee> AssignedEmployees => _assignedEmployees.AsReadOnly();
    public IReadOnlyList<PlannedService> PlannedServices => _plannedServices.AsReadOnly();

    public bool IsPerLanding => _plannedServices.Any(s => s.IsAircraftPerLanding);
    public bool IsUpdateLocked => Status is FlightStatus.Completed or FlightStatus.Canceled or FlightStatus.Merged;
    public bool CanEditScheduledDetails => Status == FlightStatus.Scheduled;

    public static Result<Flight> ScheduleNew(
        CustomerSnapshot customer,
        StationSnapshot station,
        OperationTypeSnapshot operationType,
        FlightNumber flightNumber,
        ScheduledTime schedule,
        AircraftTypeSnapshot? aircraftType,
        IReadOnlyList<ServiceSnapshot> plannedServices,
        IReadOnlyList<StaffMemberSnapshot> assignedEmployees,
        Guid? contractId,
        string? contractNumber,
        Guid createdByUserId,
        DateTimeOffset now,
        Guid? id = null)
    {
        var perLanding = PerLandingPolicy.ValidatePlannedServices(plannedServices);
        if (perLanding.IsFailure)
            return perLanding.Error;

        var flightId = id ?? Guid.NewGuid();
        var flight = new Flight
        {
            Id = flightId,
            Customer = customer,
            Station = station,
            OperationType = operationType,
            FlightNumber = flightNumber,
            OriginalFlightNumber = flightNumber.Value,
            Schedule = schedule,
            AircraftType = aircraftType,
            Status = FlightStatus.Scheduled,
            ContractId = contractId,
            ContractNumber = contractNumber,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = now
        };

        flight.ReplacePlannedServicesInternal(plannedServices);
        flight.ReplaceAssignedEmployeesInternal(assignedEmployees);
        flight.RaiseDomainEvent(new FlightScheduled(flightId));
        return flight;
    }

    public Result ChangeFlightNumber(FlightNumber newNumber, DateTimeOffset now)
    {
        var editCheck = EnsureScheduledDetailsEditable();
        if (editCheck.IsFailure)
            return editCheck.Error;

        if (FlightNumber == newNumber)
            return Result.Success();

        var previous = FlightNumber.Value;
        FlightNumber = newNumber;
        UpdatedAtUtc = now;
        RaiseDomainEvent(new FlightNumberChanged(Id, OriginalFlightNumber, newNumber.Value));
        _ = previous;
        return Result.Success();
    }

    public Result UpdateSchedule(ScheduledTime schedule, AircraftTypeSnapshot? aircraftType, DateTimeOffset now)
    {
        var editCheck = EnsureScheduledDetailsEditable();
        if (editCheck.IsFailure)
            return editCheck.Error;

        Schedule = schedule;
        AircraftType = aircraftType;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result ReplacePlannedServices(IReadOnlyList<ServiceSnapshot> plannedServices, DateTimeOffset now)
    {
        var editCheck = EnsureScheduledDetailsEditable();
        if (editCheck.IsFailure)
            return editCheck.Error;

        var perLanding = PerLandingPolicy.ValidatePlannedServices(plannedServices);
        if (perLanding.IsFailure)
            return perLanding.Error;

        ReplacePlannedServicesInternal(plannedServices);
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result AssignEmployees(IReadOnlyList<StaffMemberSnapshot> employees, DateTimeOffset now)
    {
        if (IsUpdateLocked)
            return LockedError();

        foreach (var employee in employees)
        {
            if (_assignedEmployees.Any(e => e.Employee.StaffMemberId == employee.StaffMemberId))
                continue;

            _assignedEmployees.Add(new FlightAssignedEmployee(Guid.NewGuid(), Id, employee));
            RaiseDomainEvent(new EmployeeAssignedToFlight(Id, employee.StaffMemberId));
        }

        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result RemoveAssignment(Guid staffMemberId, DateTimeOffset now)
    {
        if (IsUpdateLocked)
            return LockedError();

        var existing = _assignedEmployees.FirstOrDefault(e => e.Employee.StaffMemberId == staffMemberId);
        if (existing is null)
            return Error.NotFound("The staff member is not assigned to this flight.", "Operations.Flight.AssignmentNotFound");

        _assignedEmployees.Remove(existing);
        UpdatedAtUtc = now;
        return Result.Success();
    }

    /// <summary>Records the caller claiming a Per-Landing flight (adds them to the roster).</summary>
    public Result Claim(StaffMemberSnapshot employee, DateTimeOffset now)
    {
        if (!IsPerLanding)
            return Error.Conflict("Only Per Landing flights can be claimed.", "Operations.Flight.NotPerLanding");

        return AssignEmployees([employee], now);
    }

    public Result MarkMergedInto(Guid survivorFlightId, DateTimeOffset now)
    {
        if (Status == FlightStatus.Merged)
            return Error.Conflict("Flight is already merged.", "Operations.Flight.AlreadyMerged");

        Status = FlightStatus.Merged;
        MergedIntoFlightId = survivorFlightId;
        UpdatedAtUtc = now;
        RaiseDomainEvent(new FlightMerged(Id, survivorFlightId));
        return Result.Success();
    }

    private void ReplacePlannedServicesInternal(IReadOnlyList<ServiceSnapshot> plannedServices)
    {
        _plannedServices.Clear();
        foreach (var service in plannedServices.GroupBy(s => s.ServiceId).Select(g => g.First()))
            _plannedServices.Add(new PlannedService(Guid.NewGuid(), Id, service));
    }

    private void ReplaceAssignedEmployeesInternal(IReadOnlyList<StaffMemberSnapshot> employees)
    {
        _assignedEmployees.Clear();
        foreach (var employee in employees.GroupBy(e => e.StaffMemberId).Select(g => g.First()))
            _assignedEmployees.Add(new FlightAssignedEmployee(Guid.NewGuid(), Id, employee));
    }

    public Result EnsureScheduledDetailsEditable()
    {
        if (IsUpdateLocked)
            return LockedError();

        return CanEditScheduledDetails
            ? Result.Success()
            : Error.Conflict("Only scheduled flights can be edited.", "Operations.Flight.NotEditable");
    }

    private static Error LockedError() =>
        Error.Conflict("This flight is settled and can no longer be modified.", "Operations.Flight.Locked");
}
