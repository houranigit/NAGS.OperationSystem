using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Features.AircraftType;
using Core.Contracts.Features.Customer;
using Core.Contracts.Features.Employee;
using Core.Contracts.Features.OperationType;
using Core.Contracts.Features.Service;
using Core.Contracts.Features.Station;
using Operations.Domain.Entities;
using Operations.Domain.Enumerations;
using Operations.Domain.Events;
using Operations.Domain.ValueObjects;
using WorkOrderId = Operations.Domain.Aggregates.WorkOrder.WorkOrderId;

namespace Operations.Domain.Aggregates.Flight;

public sealed class Flight : AggregateRoot<FlightId>
{
    private List<FlightAssignedEmployee> _assignedEmployees = [];
    private List<FlightWorkOrderAttachment> _attachedWorkOrders = [];
    private List<FlightService> _services = [];

    private Flight()
    {
    }

    public CustomerSnapshot Customer { get; private set; } = null!;
    public StationSnapshot Station { get; private set; } = null!;
    public OperationTypeSnapshot OperationType { get; private set; } = null!;
    public FlightNumber FlightNumber { get; private set; } = null!;
    public ScheduledTime Schedule { get; private set; } = null!;
    public AircraftTypeSnapshot? AircraftType { get; private set; }
    public FlightStatus Status { get; private set; }
    public DateTimeOffset? CanceledAt { get; private set; }

    /// <summary>
    /// Reference to the contract this flight bills against. <c>null</c> for ad-hoc flights
    /// created from the mobile "create work order from scratch" flow (always AdHoc OT). All
    /// scheduled flights have this set at creation time via <c>IContractReadService</c>.
    /// </summary>
    public Guid? ContractId { get; private set; }

    /// <summary>
    /// Denormalised, human-readable contract number captured when the flight was created
    /// or last updated. Mirrors <see cref="ContractId"/> nullability — populated for every
    /// scheduled flight, <c>null</c> only for ad-hoc flights. Persisted as a flight column
    /// so the flights grid / calendar can show "Contract" without joining the Contracts
    /// module.
    /// </summary>
    public string? ContractNumber { get; private set; }

    /// <summary>
    /// Client-generated idempotency key for offline-originated ad-hoc flight creation. Lets
    /// the mobile client pre-allocate the flight id while offline so a retry after an
    /// ambiguous-timeout failure (server received the create, response was dropped) does
    /// not produce a second flight. Persisted with a filtered-unique index so the database
    /// enforces uniqueness as a defence-in-depth even if two retries race past the
    /// scratch-handler pre-check. Null on portal-originated and contract-bound flights
    /// where the create path is not retried client-side.
    /// </summary>
    public Guid? ClientFlightId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Accepted approved work order; when set, flight assignments and basic info are locked for portal.</summary>
    public WorkOrderSnapshot? AcceptedWorkOrder { get; private set; }

    public IReadOnlyList<FlightAssignedEmployee> AssignedEmployees => _assignedEmployees;

    /// <summary>
    /// Contract services declared as billable for this flight (copied at create time from
    /// the resolved contract's OT-services list). Drives "AOG only ⇒ assignment optional"
    /// and the mobile work-order services pre-population.
    /// </summary>
    public IReadOnlyList<FlightService> Services => _services;

    public IReadOnlyList<WorkOrderId> AttachedWorkOrderIds =>
        _attachedWorkOrders.ConvertAll(x => x.WorkOrderId);

    public IReadOnlyList<FlightWorkOrderAttachment> AttachedWorkOrderLinks => _attachedWorkOrders;

    /// <summary>
    /// Creates a flight bound to a resolved contract. Pass <c>contractId = null</c> only for
    /// ad-hoc flights (mobile create-from-scratch). The <paramref name="services"/> list is
    /// the contract's services for this OT (or empty for ad-hoc) and is the source-of-truth
    /// for the work-order services step.
    /// </summary>
    /// <param name="assignmentRequired">
    /// When true (the default for non-AOG-only contracts), at least one assigned employee
    /// is required. When false (AOG-only contract or ad-hoc creation), an empty assigned
    /// list is allowed.
    /// </param>
    public static Result<Flight> Create(
        Guid? contractId,
        string? contractNumber,
        CustomerSnapshot customer,
        StationSnapshot station,
        OperationTypeSnapshot operationType,
        FlightNumber flightNumber,
        ScheduledTime schedule,
        AircraftTypeSnapshot? aircraftType,
        IReadOnlyList<ServiceSnapshot> services,
        IReadOnlyList<EmployeeSnapshot> assignedEmployees,
        bool assignmentRequired,
        DateTimeOffset now)
    {
        // ContractId and ContractNumber must be paired: either both null (ad-hoc) or both
        // populated (scheduled). Mismatched values would corrupt the flight's billing
        // snapshot and the flight grid would show a number that doesn't resolve.
        if (contractId.HasValue ^ !string.IsNullOrWhiteSpace(contractNumber))
            return Error.Validation("Flight contract id and contract number must be set together.");

        var id = FlightId.New();
        var flight = new Flight
        {
            Id = id,
            ContractId = contractId,
            ContractNumber = string.IsNullOrWhiteSpace(contractNumber) ? null : contractNumber,
            Customer = customer,
            Station = station,
            OperationType = operationType,
            FlightNumber = flightNumber,
            Schedule = schedule,
            AircraftType = aircraftType,
            Status = FlightStatus.Scheduled,
            CanceledAt = null,
            AcceptedWorkOrder = null,
            CreatedAt = now,
            UpdatedAt = now
        };

        var sync = flight.SyncAssignedEmployees(assignedEmployees, assignmentRequired);
        if (sync.IsFailure)
            return sync.Error;

        // Newly-added employees on creation get the same notification as an explicit invite.
        // The previous-employee set is empty here, so every assignee is a "new" arrival.
        foreach (var emp in flight._assignedEmployees)
            flight.RaiseDomainEvent(new EmployeeInvitedToFlightEvent(id, Guid.Empty, emp.Employee.EmployeeId));

        flight._services = services
            .Select(s => new FlightService(Guid.NewGuid(), id, s))
            .ToList();

        flight.RaiseDomainEvent(new FlightCreatedEvent(id));
        return flight;
    }

    public bool IsUpdateLocked => AcceptedWorkOrder is not null;

    /// <summary>
    /// Replaces assigned employees with the given list (full sync). Used by create/update flows.
    /// </summary>
    public Result SyncAssignedEmployees(IReadOnlyList<EmployeeSnapshot> employees, bool assignmentRequired)
    {
        if (assignmentRequired && employees.Count < 1)
            return Error.Validation("At least one assigned employee is required.");

        _assignedEmployees.Clear();
        foreach (var e in employees)
            _assignedEmployees.Add(new FlightAssignedEmployee(Guid.NewGuid(), Id, e));

        return Result.Success();
    }

    /// <summary>
    /// Replaces operational details and assigned employees in one shot. Compares old vs new
    /// assignees and raises <see cref="EmployeeInvitedToFlightEvent"/> for every newcomer so
    /// notifications are consistent with the dedicated <see cref="InviteEmployee"/> flow.
    /// </summary>
    public Result UpdateOperationalDetails(
        Guid? contractId,
        string? contractNumber,
        CustomerSnapshot customer,
        StationSnapshot station,
        OperationTypeSnapshot operationType,
        FlightNumber flightNumber,
        ScheduledTime schedule,
        AircraftTypeSnapshot? aircraftType,
        IReadOnlyList<ServiceSnapshot> services,
        IReadOnlyList<EmployeeSnapshot> assignedEmployees,
        bool assignmentRequired,
        Guid actingEmployeeId)
    {
        if (IsUpdateLocked)
            return Error.Conflict("Flight cannot be updated while a work order is accepted.");

        if (contractId.HasValue ^ !string.IsNullOrWhiteSpace(contractNumber))
            return Error.Validation("Flight contract id and contract number must be set together.");

        var previousIds = _assignedEmployees
            .Select(x => x.Employee.EmployeeId)
            .ToHashSet();

        var sync = SyncAssignedEmployees(assignedEmployees, assignmentRequired);
        if (sync.IsFailure)
            return sync;

        // Notify every employee added by this update — same path as an explicit invite.
        foreach (var emp in _assignedEmployees)
        {
            if (!previousIds.Contains(emp.Employee.EmployeeId))
                RaiseDomainEvent(new EmployeeInvitedToFlightEvent(Id, actingEmployeeId, emp.Employee.EmployeeId));
        }

        ContractId = contractId;
        ContractNumber = string.IsNullOrWhiteSpace(contractNumber) ? null : contractNumber;
        Customer = customer;
        Station = station;
        OperationType = operationType;
        FlightNumber = flightNumber;
        Schedule = schedule;
        AircraftType = aircraftType;

        _services = services
            .Select(s => new FlightService(Guid.NewGuid(), Id, s))
            .ToList();

        Touch();
        return Result.Success();
    }

    public Result AssignEmployee(EmployeeSnapshot employee)
    {
        if (IsUpdateLocked)
            return Error.Conflict("Flight cannot be updated while a work order is accepted.");

        if (_assignedEmployees.Any(x => x.Employee.EmployeeId == employee.EmployeeId))
            return Result.Success();

        _assignedEmployees.Add(new FlightAssignedEmployee(Guid.NewGuid(), Id, employee));
        // Notify the newly-added employee; inviter unknown at this code path, use Empty.
        RaiseDomainEvent(new EmployeeInvitedToFlightEvent(Id, Guid.Empty, employee.EmployeeId));
        Touch();
        return Result.Success();
    }

    /// <summary>Assigns if missing and raises <see cref="EmployeeInvitedToFlightEvent"/>.</summary>
    public Result InviteEmployee(EmployeeSnapshot invitee, Guid inviterEmployeeId)
    {
        if (IsUpdateLocked)
            return Error.Conflict("Flight cannot be updated while a work order is accepted.");

        if (inviterEmployeeId == Guid.Empty)
            return Error.Validation("Inviter is required.");

        if (_assignedEmployees.Any(x => x.Employee.EmployeeId == invitee.EmployeeId))
            return Result.Success();

        _assignedEmployees.Add(new FlightAssignedEmployee(Guid.NewGuid(), Id, invitee));
        RaiseDomainEvent(new EmployeeInvitedToFlightEvent(Id, inviterEmployeeId, invitee.EmployeeId));
        Touch();
        return Result.Success();
    }

    public Result RemoveAssignedEmployee(Guid employeeId, bool assignmentRequired)
    {
        if (IsUpdateLocked)
            return Error.Conflict("Flight cannot be updated while a work order is accepted.");

        if (employeeId == Guid.Empty)
            return Error.Validation("Employee is required.");

        var rest = _assignedEmployees.Where(x => x.Employee.EmployeeId != employeeId).ToList();
        if (assignmentRequired && rest.Count < 1)
            return Error.Validation("At least one assigned employee is required.");
        if (rest.Count == _assignedEmployees.Count)
            return Error.Validation("Employee is not assigned to this flight.");

        _assignedEmployees.Clear();
        foreach (var a in rest)
            _assignedEmployees.Add(a);
        Touch();
        return Result.Success();
    }

    public Result AttachWorkOrder(WorkOrderId workOrderId)
    {
        if (_attachedWorkOrders.Any(w => w.WorkOrderId == workOrderId))
            return Result.Success();

        _attachedWorkOrders.Add(new FlightWorkOrderAttachment(Guid.NewGuid(), Id, workOrderId));
        if (Status == FlightStatus.Scheduled)
            Status = FlightStatus.InProgress;
        RaiseDomainEvent(new WorkOrderAttachedToFlightEvent(Id, workOrderId));
        Touch();
        return Result.Success();
    }

    public Result DetachWorkOrder(WorkOrderId workOrderId)
    {
        var n = _attachedWorkOrders.RemoveAll(w => w.WorkOrderId == workOrderId);
        if (n == 0)
            return Error.Validation("Work order is not attached to this flight.");
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Advances <see cref="UpdatedAt"/> when a linked work order changes without any flight
    /// field mutation — mobile sync uses this timestamp as the envelope version so edits
    /// to an under-review work order still produce a fresh upsert cursor.
    /// </summary>
    public Result NotifyLinkedWorkOrderChanged()
    {
        Touch();
        return Result.Success();
    }

    /// <summary>Final settlement from an approved work order. Issues snapshot and completed/canceled state.</summary>
    public Result SetSettledWorkOrder(WorkOrderSnapshot snapshot, bool workOrderIsCanceled, DateTimeOffset now)
    {
        AcceptedWorkOrder = snapshot;
        Status = workOrderIsCanceled ? FlightStatus.Canceled : FlightStatus.Completed;
        if (workOrderIsCanceled)
            CanceledAt = now;
        else
            CanceledAt = null;

        RaiseDomainEvent(new FlightSettledFromWorkOrderEvent(Id, snapshot));
        Touch();
        return Result.Success();
    }

    /// <summary>When an approved work order is revoked, flight returns to in progress if it was completed/canceled only by that settlement.</summary>
    public Result ClearAcceptedWorkOrderForRevoke()
    {
        if (AcceptedWorkOrder is null)
            return Error.Conflict("Flight has no accepted work order to clear.");

        AcceptedWorkOrder = null;
        if (Status is FlightStatus.Completed or FlightStatus.Canceled)
        {
            Status = FlightStatus.InProgress;
            CanceledAt = null;
        }
        Touch();
        return Result.Success();
    }

    public Result CancelOperatively(DateTimeOffset at)
    {
        if (Status == FlightStatus.Canceled)
            return Error.Conflict("Flight is already canceled.");
        if (AcceptedWorkOrder is not null)
            return Error.Conflict("Cancel is not allowed after a work order is accepted.");

        Status = FlightStatus.Canceled;
        CanceledAt = at;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Creates an in-progress flight with services + an empty assigned list (mobile
    /// "create from scratch" path — always AdHoc OT). Does <em>not</em> raise invite events
    /// for the creator; the caller (claim/scratch handler) attaches the work order which
    /// in turn records the creator as the WO's <c>CreatedByEmployeeId</c>.
    /// </summary>
    public static Result<Flight> CreateAdHoc(
        CustomerSnapshot customer,
        StationSnapshot station,
        OperationTypeSnapshot operationType,
        FlightNumber flightNumber,
        ScheduledTime schedule,
        AircraftTypeSnapshot? aircraftType,
        EmployeeSnapshot creatorEmployee,
        DateTimeOffset now,
        Guid? clientFlightId = null)
    {
        var id = FlightId.New();
        var flight = new Flight
        {
            Id = id,
            ContractId = null,
            ContractNumber = null,
            ClientFlightId = clientFlightId == Guid.Empty ? null : clientFlightId,
            Customer = customer,
            Station = station,
            OperationType = operationType,
            FlightNumber = flightNumber,
            Schedule = schedule,
            AircraftType = aircraftType,
            Status = FlightStatus.InProgress,
            CanceledAt = null,
            AcceptedWorkOrder = null,
            CreatedAt = now,
            UpdatedAt = now
        };

        flight._assignedEmployees.Add(new FlightAssignedEmployee(Guid.NewGuid(), id, creatorEmployee));
        flight.RaiseDomainEvent(new FlightCreatedEvent(id));
        return flight;
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
