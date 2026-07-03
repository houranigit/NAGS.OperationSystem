using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Operations.Domain.Enumerations;
using Operations.Domain.Events;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.WorkOrders;

/// <summary>
/// The operational completion document for a flight. Its outcome is a normal completion or a
/// cancellation. Flows Draft -> Submitted -> Approved (locked, numbered, sent to billing); an admin
/// may Return an approved work order to review to unlock it. Losers of a duplicate merge become Superseded.
/// </summary>
public sealed class WorkOrder : AggregateRoot<Guid>
{
    private readonly List<WorkOrderServiceLine> _serviceLines = [];
    private readonly List<WorkOrderTask> _tasks = [];

    private WorkOrder() { }

    public Guid FlightId { get; private set; }
    public WorkOrderType Type { get; private set; }
    public WorkOrderStatus Status { get; private set; }
    public WorkOrderNumber? Number { get; private set; }

    public CustomerSnapshot Customer { get; private set; } = null!;
    public StationSnapshot Station { get; private set; } = null!;
    public OperationTypeSnapshot OperationType { get; private set; } = null!;
    public FlightNumber FlightNumber { get; private set; } = null!;
    public AircraftTypeSnapshot? AircraftType { get; private set; }
    public string? AircraftTailNumber { get; private set; }
    public ScheduledTime Schedule { get; private set; } = null!;
    public ActualTime? Actuals { get; private set; }
    public CancellationDetails? Cancellation { get; private set; }
    public string? Remarks { get; private set; }

    /// <summary>Reference to a stored customer-signature/acknowledgement file (optional).</summary>
    public string? CustomerSignatureReference { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public Guid? SupersededByWorkOrderId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<WorkOrderServiceLine> ServiceLines => _serviceLines.AsReadOnly();
    public IReadOnlyList<WorkOrderTask> Tasks => _tasks.AsReadOnly();

    public bool IsCancellation => Type == WorkOrderType.Cancellation;
    public bool IsEditable => Status is WorkOrderStatus.Draft or WorkOrderStatus.Returned;

    public static WorkOrder OpenCompletion(FlightContext flight, Guid createdByUserId, DateTimeOffset now, Guid? id = null)
    {
        var workOrder = NewFrom(flight, WorkOrderType.Completion, createdByUserId, now, id);
        workOrder.RaiseDomainEvent(new WorkOrderOpened(workOrder.Id, flight.FlightId));
        return workOrder;
    }

    public static WorkOrder OpenCancellation(FlightContext flight, CancellationDetails cancellation, Guid createdByUserId, DateTimeOffset now, Guid? id = null)
    {
        var workOrder = NewFrom(flight, WorkOrderType.Cancellation, createdByUserId, now, id);
        workOrder.Cancellation = cancellation;
        workOrder.RaiseDomainEvent(new WorkOrderOpened(workOrder.Id, flight.FlightId));
        return workOrder;
    }

    private static WorkOrder NewFrom(FlightContext flight, WorkOrderType type, Guid createdByUserId, DateTimeOffset now, Guid? id)
    {
        return new WorkOrder
        {
            Id = id ?? Guid.NewGuid(),
            FlightId = flight.FlightId,
            Type = type,
            Status = WorkOrderStatus.Draft,
            Customer = flight.Customer,
            Station = flight.Station,
            OperationType = flight.OperationType,
            FlightNumber = flight.FlightNumber,
            AircraftType = flight.AircraftType,
            Schedule = flight.Schedule,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = now
        };
    }

    public Result ReplaceServiceLines(IReadOnlyList<ServiceLineInput> inputs, DateTimeOffset now)
    {
        if (!IsEditable)
            return NotEditable();

        var built = new List<WorkOrderServiceLine>();
        foreach (var input in inputs)
        {
            var line = WorkOrderServiceLine.Create(Id, input);
            if (line.IsFailure)
                return line.Error;
            built.Add(line.Value);
        }

        _serviceLines.Clear();
        _serviceLines.AddRange(built);
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result ReplaceTasks(IReadOnlyList<TaskInput> inputs, DateTimeOffset now)
    {
        if (!IsEditable)
            return NotEditable();

        var built = new List<WorkOrderTask>();
        foreach (var input in inputs)
        {
            var task = WorkOrderTask.Create(Id, input);
            if (task.IsFailure)
                return task.Error;
            built.Add(task.Value);
        }

        _tasks.Clear();
        _tasks.AddRange(built);
        UpdatedAtUtc = now;
        return Result.Success();
    }

    /// <summary>Appends Return-to-Ramp follow-up lines/tasks (ReturnToRamp forced true).</summary>
    public Result AppendReturnToRamp(IReadOnlyList<ServiceLineInput> serviceInputs, IReadOnlyList<TaskInput> taskInputs, DateTimeOffset now)
    {
        if (!IsEditable)
            return NotEditable();

        if (serviceInputs.Count == 0 && taskInputs.Count == 0)
            return Error.Validation("Return to ramp requires at least one line or task.", "Operations.ReturnToRamp.Empty");

        foreach (var input in serviceInputs)
        {
            var line = WorkOrderServiceLine.Create(Id, input with { ReturnToRamp = true });
            if (line.IsFailure)
                return line.Error;
            _serviceLines.Add(line.Value);
        }

        foreach (var input in taskInputs)
        {
            var task = WorkOrderTask.Create(Id, input with { ReturnToRamp = true });
            if (task.IsFailure)
                return task.Error;
            _tasks.Add(task.Value);
        }

        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result SetActualTimes(ActualTime actuals, DateTimeOffset now)
    {
        if (!IsEditable)
            return NotEditable();

        Actuals = actuals;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result SetAircraftTailNumber(string? tailNumber, DateTimeOffset now)
    {
        if (!IsEditable)
            return NotEditable();

        AircraftTailNumber = string.IsNullOrWhiteSpace(tailNumber) ? null : tailNumber.Trim().ToUpperInvariant();
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result SetRemarks(string? remarks, DateTimeOffset now)
    {
        if (!IsEditable)
            return NotEditable();

        Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim();
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result SetCustomerSignature(string? storageReference, DateTimeOffset now)
    {
        if (!IsEditable)
            return NotEditable();

        CustomerSignatureReference = string.IsNullOrWhiteSpace(storageReference) ? null : storageReference.Trim();
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result Submit(IReadOnlyCollection<Guid> requiredPlannedServiceIds, bool isPerLanding, DateTimeOffset now)
    {
        if (!IsEditable)
            return Error.Conflict("Only a draft or returned work order can be submitted.", "Operations.WorkOrder.NotSubmittable");

        if (Type == WorkOrderType.Completion && !isPerLanding)
        {
            var performedIds = _serviceLines.Select(l => l.Service.ServiceId).ToHashSet();
            var missing = requiredPlannedServiceIds.Where(id => !performedIds.Contains(id)).ToList();
            if (missing.Count > 0)
                return Error.Validation("A completion must include all planned services before it can be submitted.", "Operations.WorkOrder.MissingPlannedServices");
        }

        Status = WorkOrderStatus.Submitted;
        UpdatedAtUtc = now;
        RaiseDomainEvent(new WorkOrderSubmitted(Id, FlightId));
        return Result.Success();
    }

    public Result Approve(WorkOrderNumber number, Guid approvedByUserId, DateTimeOffset now)
    {
        if (Status != WorkOrderStatus.Submitted)
            return Error.Conflict("Only a submitted work order can be approved.", "Operations.WorkOrder.NotApprovable");

        if (Type == WorkOrderType.Completion && Actuals is null)
            return Error.Validation("Actual arrival and departure times are required to approve a completion.", "Operations.WorkOrder.ActualsRequired");

        if (Type == WorkOrderType.Cancellation && Cancellation is null)
            return Error.Validation("Cancellation details are required to approve a cancellation.", "Operations.WorkOrder.CancellationRequired");

        Number = number;
        Status = WorkOrderStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAtUtc = now;
        UpdatedAtUtc = now;
        RaiseDomainEvent(new WorkOrderApproved(Id, FlightId));
        return Result.Success();
    }

    public Result Reject(DateTimeOffset now)
    {
        if (Status != WorkOrderStatus.Submitted)
            return Error.Conflict("Only a submitted work order can be rejected.", "Operations.WorkOrder.NotRejectable");

        Status = WorkOrderStatus.Rejected;
        UpdatedAtUtc = now;
        RaiseDomainEvent(new WorkOrderRejected(Id, FlightId));
        return Result.Success();
    }

    /// <summary>Admin returns a submitted or approved work order to editable review, unlocking it.</summary>
    public Result ReturnToReview(DateTimeOffset now)
    {
        if (Status is not (WorkOrderStatus.Submitted or WorkOrderStatus.Approved))
            return Error.Conflict("Only a submitted or approved work order can be returned to review.", "Operations.WorkOrder.NotReturnable");

        Status = WorkOrderStatus.Returned;
        Number = null;
        ApprovedByUserId = null;
        ApprovedAtUtc = null;
        UpdatedAtUtc = now;
        RaiseDomainEvent(new WorkOrderReturnedToReview(Id, FlightId));
        return Result.Success();
    }

    /// <summary>Re-points this work order to the surviving flight during an ad-hoc flight merge.</summary>
    public void ReassignToFlight(Guid flightId, DateTimeOffset now)
    {
        FlightId = flightId;
        UpdatedAtUtc = now;
    }

    public Result Supersede(Guid survivorWorkOrderId, DateTimeOffset now)
    {
        if (Status == WorkOrderStatus.Approved)
            return Error.Conflict("An approved work order cannot be superseded; return it to review first.", "Operations.WorkOrder.ApprovedNotSupersedable");
        if (Status == WorkOrderStatus.Superseded)
            return Result.Success();

        Status = WorkOrderStatus.Superseded;
        SupersededByWorkOrderId = survivorWorkOrderId;
        UpdatedAtUtc = now;
        RaiseDomainEvent(new WorkOrderSuperseded(Id, survivorWorkOrderId));
        return Result.Success();
    }

    private static Error NotEditable() =>
        Error.Conflict("This work order is not editable in its current state.", "Operations.WorkOrder.NotEditable");
}

/// <summary>The flight data copied onto a work order when it is opened.</summary>
public sealed record FlightContext(
    Guid FlightId,
    CustomerSnapshot Customer,
    StationSnapshot Station,
    OperationTypeSnapshot OperationType,
    FlightNumber FlightNumber,
    ScheduledTime Schedule,
    AircraftTypeSnapshot? AircraftType);
