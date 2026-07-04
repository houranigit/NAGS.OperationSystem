using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Operations.Domain.Enumerations;
using Operations.Domain.Events;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;

namespace Operations.Domain.Flights;

/// <summary>
/// A serviced aircraft event (scheduled in advance or created ad-hoc). Master aggregate that owns the
/// flight lifecycle, planned services, assigned employees, and flight-number history, and references
/// its work order(s) by id. Completed/Canceled are terminal, post-approval, billable outcomes, at
/// which point the flight captures the approved work order's scalar values plus its reference and
/// becomes the billing-ready source of truth.
/// </summary>
public sealed class Flight : AggregateRoot<Guid>
{
    private readonly List<FlightAssignedEmployee> _assignedEmployees = [];
    private readonly List<PlannedService> _plannedServices = [];

    private Flight() { }

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

    /// <summary>
    /// The captured scalar values and reference of the approved work order. Set on approval, cleared
    /// when the approval is returned/reverted. Actual service lines and tasks are read from the
    /// referenced approved work order.
    /// </summary>
    public ApprovedWorkOrderSnapshot? ApprovedWorkOrder { get; private set; }

    // Merge/duplicate metadata.
    public Guid? MergedIntoFlightId { get; private set; }
    public Guid? PotentialDuplicateOfFlightId { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<FlightAssignedEmployee> AssignedEmployees => _assignedEmployees.AsReadOnly();
    public IReadOnlyList<PlannedService> PlannedServices => _plannedServices.AsReadOnly();

    public bool IsPerLanding => _plannedServices.Any(s => s.IsAircraftPerLanding);
    public bool IsUpdateLocked => Status is FlightStatus.Completed or FlightStatus.Canceled or FlightStatus.Merged;

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

    public static Result<Flight> CreateAdHoc(
        CustomerSnapshot customer,
        StationSnapshot station,
        OperationTypeSnapshot operationType,
        FlightNumber flightNumber,
        ScheduledTime schedule,
        AircraftTypeSnapshot? aircraftType,
        IReadOnlyList<ServiceSnapshot> plannedServices,
        StaffMemberSnapshot creator,
        Guid createdByUserId,
        DateTimeOffset now,
        bool allowEmptyPlannedServices = false,
        Guid? id = null)
    {
        // An ad-hoc flight created together with a cancellation work order may carry no planned
        // services; every other creation path requires at least one.
        var perLanding = PerLandingPolicy.ValidatePlannedServices(plannedServices, allowEmpty: allowEmptyPlannedServices);
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
            Status = FlightStatus.InProgress,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = now
        };

        flight.ReplacePlannedServicesInternal(plannedServices);
        flight.ReplaceAssignedEmployeesInternal([creator]);
        flight.RaiseDomainEvent(new AdHocFlightCreated(flightId));
        return flight;
    }

    public Result ChangeFlightNumber(FlightNumber newNumber, DateTimeOffset now)
    {
        if (IsUpdateLocked)
            return LockedError();

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
        if (IsUpdateLocked)
            return LockedError();

        Schedule = schedule;
        AircraftType = aircraftType;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result ReplacePlannedServices(IReadOnlyList<ServiceSnapshot> plannedServices, DateTimeOffset now)
    {
        if (IsUpdateLocked)
            return LockedError();

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

    // --- Lifecycle transitions driven by the work order ------------------------
    // Opening/authoring a draft work order does NOT change the flight status; the flight moves to
    // InProgress only when a work order is submitted.

    public void OnWorkOrderSubmitted(DateTimeOffset now)
    {
        if (Status == FlightStatus.Scheduled || Status == FlightStatus.InProgress)
        {
            Status = FlightStatus.InProgress;
            UpdatedAtUtc = now;
        }
    }

    public void OnWorkOrderReturnedToReview(DateTimeOffset now)
    {
        if (Status is FlightStatus.Scheduled or FlightStatus.InProgress)
        {
            Status = FlightStatus.InProgress;
            UpdatedAtUtc = now;
        }
    }

    /// <summary>Settles the flight as Completed, capturing the approved work order's values and reference.</summary>
    public Result SettleCompleted(WorkOrder workOrder, DateTimeOffset now)
    {
        var snapshot = BuildApprovedSnapshot(workOrder);
        if (snapshot.IsFailure)
            return snapshot.Error;

        if (workOrder.IsCancellation)
            return Error.Conflict("A cancellation work order cannot complete a flight.", "Operations.Flight.WrongOutcome");

        ApprovedWorkOrder = snapshot.Value;
        Status = FlightStatus.Completed;
        UpdatedAtUtc = now;
        RaiseDomainEvent(new FlightCompleted(Id, workOrder.Id));
        return Result.Success();
    }

    /// <summary>Settles the flight as Canceled, capturing the approved cancellation work order's values and reference.</summary>
    public Result SettleCanceled(WorkOrder workOrder, DateTimeOffset now)
    {
        var snapshot = BuildApprovedSnapshot(workOrder);
        if (snapshot.IsFailure)
            return snapshot.Error;

        if (!workOrder.IsCancellation)
            return Error.Conflict("Only a cancellation work order can cancel a flight.", "Operations.Flight.WrongOutcome");

        ApprovedWorkOrder = snapshot.Value;
        Status = FlightStatus.Canceled;
        UpdatedAtUtc = now;
        RaiseDomainEvent(new FlightCanceled(Id, workOrder.Id));
        return Result.Success();
    }

    /// <summary>
    /// Clears the captured approved work order values and reference when the approval is
    /// returned/reverted, and returns the flight to InProgress.
    /// </summary>
    public Result ClearApprovedSnapshot(DateTimeOffset now)
    {
        if (ApprovedWorkOrder is null)
            return Error.Conflict("This flight has no approved work order to clear.", "Operations.Flight.NoApprovedSnapshot");

        var clearedWorkOrderId = ApprovedWorkOrder.WorkOrderId;
        ApprovedWorkOrder = null;
        Status = FlightStatus.InProgress;
        UpdatedAtUtc = now;
        RaiseDomainEvent(new ApprovedWorkOrderSnapshotCleared(Id, clearedWorkOrderId));
        return Result.Success();
    }

    private Result<ApprovedWorkOrderSnapshot> BuildApprovedSnapshot(WorkOrder workOrder)
    {
        if (workOrder.FlightId != Id)
            return Error.Conflict("The work order does not belong to this flight.", "Operations.Flight.WorkOrderMismatch");

        if (workOrder.Status != WorkOrderStatus.Approved || workOrder.Number is null ||
            workOrder.ApprovedByUserId is not { } approvedBy || workOrder.ApprovedAtUtc is not { } approvedAt)
        {
            return Error.Conflict("Only an approved, numbered work order can settle a flight.", "Operations.Flight.WorkOrderNotApproved");
        }

        return new ApprovedWorkOrderSnapshot(
            workOrder.Id,
            workOrder.Number.Value,
            workOrder.Type,
            workOrder.FlightNumber.Value,
            workOrder.AircraftType?.AircraftTypeId,
            workOrder.AircraftType?.Manufacturer,
            workOrder.AircraftType?.Model,
            workOrder.AircraftTailNumber,
            workOrder.Actuals?.Ata,
            workOrder.Actuals?.Atd,
            workOrder.Remarks,
            workOrder.CustomerSignatureReference,
            workOrder.Cancellation?.CanceledByUserId,
            workOrder.Cancellation?.CanceledAtUtc,
            workOrder.Cancellation?.Reason,
            approvedBy,
            approvedAt);
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

    public void FlagPotentialDuplicate(Guid otherFlightId, DateTimeOffset now)
    {
        PotentialDuplicateOfFlightId = otherFlightId;
        UpdatedAtUtc = now;
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

    private static Error LockedError() =>
        Error.Conflict("This flight is settled and can no longer be modified.", "Operations.Flight.Locked");
}
