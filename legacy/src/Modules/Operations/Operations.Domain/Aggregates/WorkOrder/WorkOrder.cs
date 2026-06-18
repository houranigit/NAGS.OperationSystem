using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Features.AircraftType;
using Core.Contracts.Features.Customer;
using Core.Contracts.Features.OperationType;
using Core.Contracts.Features.Station;
using Operations.Domain.Entities;
using Operations.Domain.Enumerations;
using Operations.Domain.Events;
using Operations.Domain.ValueObjects;
using OpsFlight = Operations.Domain.Aggregates.Flight;

namespace Operations.Domain.Aggregates.WorkOrder;

public sealed class WorkOrder : AggregateRoot<WorkOrderId>
{
    private List<WorkOrderServiceLine> _serviceLines = [];
    private List<WorkOrderTask> _tasks = [];

    private WorkOrder()
    {
    }

    public OpsFlight.FlightId? FlightId { get; private set; }
    public OpsFlight.FlightId? ConfirmedFlightId { get; private set; }
    public WorkOrderNumber? WorkOrderNo { get; private set; }
    public CustomerSnapshot Customer { get; private set; } = null!;
    public StationSnapshot Station { get; private set; } = null!;
    public OperationTypeSnapshot OperationType { get; private set; } = null!;
    public FlightNumber FlightNumber { get; private set; } = null!;
    public AircraftTypeSnapshot? AircraftType { get; private set; }
    public string? AircraftTailNumber { get; private set; }
    public ScheduledTime Schedule { get; private set; } = null!;
    public ActualTime? TimesActual { get; private set; }
    public bool IsCanceled { get; private set; }
    public DateTimeOffset? CanceledAt { get; private set; }
    public WorkOrderStatus Status { get; private set; }

    /// <summary>Free-form remarks recorded by the author. Optional.</summary>
    public string? Remarks { get; private set; }

    /// <summary>
    /// Employee that authored the work order — set on mobile-originated submissions so the
    /// /api/mobile/flights/{id}/context query can scope "my under-review work order" without
    /// leaking other employees' work orders. Null on portal-originated work orders or legacy rows.
    /// </summary>
    public Guid? CreatedByEmployeeId { get; private set; }

    /// <summary>
    /// Raw PNG bytes of the customer signature captured on the mobile app before submission.
    /// Optional — null means the customer was not asked to sign (or the work order originates
    /// from the portal where signature capture is not available). Stored as <c>varbinary(max)</c>
    /// so the portal review dialog can render it inline as a data URL.
    /// </summary>
    public byte[]? CustomerSignature { get; private set; }

    /// <summary>
    /// Client-generated idempotency key for mobile-originated submissions. Set on the row
    /// the first time the request is accepted; subsequent retries that carry the same key
    /// short-circuit in the create handler and return this work order's id again instead of
    /// producing a duplicate. Persisted with a filtered-unique index so the database
    /// enforces uniqueness as a defence-in-depth even if two retries race past the handler
    /// pre-check. Null on portal-originated and server-job submissions where the client has
    /// no retry pipeline that needs deduplication.
    /// </summary>
    public Guid? ClientMutationId { get; private set; }

    /// <summary>UTC instant the work order entered <see cref="WorkOrderStatus.Deleting"/>; null in any other state.</summary>
    public DateTimeOffset? MarkedForDeletionAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<WorkOrderServiceLine> ServiceLines => _serviceLines;

    /// <summary>
    /// Unified task list. Each task carries its own type (Major/Minor), description, time
    /// window, participating employees, optional tools/materials/general supports,
    /// attachments, and RTR flag. Replaces the legacy <c>EmployeeLines</c> and
    /// <c>CorrectiveActions</c> collections.
    /// </summary>
    public IReadOnlyList<WorkOrderTask> Tasks => _tasks;

    public static Result<WorkOrder> Create(
        CustomerSnapshot customer,
        StationSnapshot station,
        OperationTypeSnapshot operationType,
        FlightNumber flightNumber,
        AircraftTypeSnapshot? aircraftType,
        string? aircraftTailNumber,
        ScheduledTime schedule,
        IReadOnlyList<WorkOrderServiceLineInput> serviceLines,
        IReadOnlyList<WorkOrderTaskInput> tasks,
        bool isCanceled,
        DateTimeOffset? cancellationAt,
        ActualTime? actualTime,
        DateTimeOffset utcNow,
        string? remarks = null,
        Guid? createdByEmployeeId = null,
        byte[]? customerSignature = null,
        Guid? clientMutationId = null) =>
        CreateCore(
            null,
            customer,
            station,
            operationType,
            flightNumber,
            aircraftType,
            aircraftTailNumber,
            schedule,
            serviceLines,
            tasks,
            isCanceled,
            cancellationAt,
            actualTime,
            utcNow,
            remarks,
            createdByEmployeeId,
            customerSignature,
            clientMutationId);

    public static Result<WorkOrder> CreateWithFlight(
        OpsFlight.FlightId flightId,
        CustomerSnapshot customer,
        StationSnapshot station,
        OperationTypeSnapshot operationType,
        FlightNumber flightNumber,
        AircraftTypeSnapshot? aircraftType,
        string? aircraftTailNumber,
        ScheduledTime schedule,
        IReadOnlyList<WorkOrderServiceLineInput> serviceLines,
        IReadOnlyList<WorkOrderTaskInput> tasks,
        bool isCanceled,
        DateTimeOffset? cancellationAt,
        ActualTime? actualTime,
        DateTimeOffset utcNow,
        string? remarks = null,
        Guid? createdByEmployeeId = null,
        byte[]? customerSignature = null,
        Guid? clientMutationId = null) =>
        CreateCore(
            flightId,
            customer,
            station,
            operationType,
            flightNumber,
            aircraftType,
            aircraftTailNumber,
            schedule,
            serviceLines,
            tasks,
            isCanceled,
            cancellationAt,
            actualTime,
            utcNow,
            remarks,
            createdByEmployeeId,
            customerSignature,
            clientMutationId);

    private static Result<WorkOrder> CreateCore(
        OpsFlight.FlightId? flightId,
        CustomerSnapshot customer,
        StationSnapshot station,
        OperationTypeSnapshot operationType,
        FlightNumber flightNumber,
        AircraftTypeSnapshot? aircraftType,
        string? aircraftTailNumber,
        ScheduledTime schedule,
        IReadOnlyList<WorkOrderServiceLineInput> serviceLines,
        IReadOnlyList<WorkOrderTaskInput> tasks,
        bool isCanceled,
        DateTimeOffset? cancellationAt,
        ActualTime? actualTime,
        DateTimeOffset utcNow,
        string? remarks,
        Guid? createdByEmployeeId,
        byte[]? customerSignature,
        Guid? clientMutationId)
    {
        var timeCheck = ValidateActuals(isCanceled, actualTime, cancellationAt);
        if (timeCheck.IsFailure)
            return timeCheck.Error;

        // Lines are optional — a freshly-created work order can sit in UnderReview with zero
        // rows (used by the cancel-flight flow where the WO carries only the cancellation
        // metadata until it is approved).
        var id = WorkOrderId.New();
        var sync = SyncLineItemsCore(id, serviceLines, tasks);
        if (sync.IsFailure)
            return sync.Error;

        var w = new WorkOrder
        {
            Id = id,
            FlightId = flightId,
            ConfirmedFlightId = null,
            WorkOrderNo = null,
            Customer = customer,
            Station = station,
            OperationType = operationType,
            FlightNumber = flightNumber,
            AircraftType = aircraftType,
            AircraftTailNumber = string.IsNullOrWhiteSpace(aircraftTailNumber) ? null : aircraftTailNumber.Trim().ToUpperInvariant(),
            Schedule = schedule,
            TimesActual = actualTime,
            IsCanceled = isCanceled,
            CanceledAt = isCanceled ? cancellationAt : null,
            Status = WorkOrderStatus.UnderReview,
            Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim(),
            CreatedByEmployeeId = createdByEmployeeId == Guid.Empty ? null : createdByEmployeeId,
            CustomerSignature = customerSignature is { Length: > 0 } ? customerSignature : null,
            ClientMutationId = clientMutationId == Guid.Empty ? null : clientMutationId,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
        w._serviceLines.AddRange(sync.Value!.ServiceLines);
        w._tasks.AddRange(sync.Value.Tasks);
        return w;
    }

    /// <summary>Rebuilds service lines and tasks (full sync). Only valid while UnderReview.</summary>
    public Result SyncLineItems(
        IReadOnlyList<WorkOrderServiceLineInput> serviceLines,
        IReadOnlyList<WorkOrderTaskInput> tasks)
    {
        var built = SyncLineItemsCore(Id, serviceLines, tasks);
        if (built.IsFailure)
            return built.Error;

        _serviceLines.Clear();
        _serviceLines.AddRange(built.Value!.ServiceLines);
        _tasks.Clear();
        _tasks.AddRange(built.Value.Tasks);
        Touch();
        return Result.Success();
    }

    public Result UpdateBasicInfo(
        CustomerSnapshot customer,
        StationSnapshot station,
        OperationTypeSnapshot operationType,
        FlightNumber flightNumber,
        AircraftTypeSnapshot? aircraftType,
        string? aircraftTailNumber,
        ScheduledTime schedule,
        IReadOnlyList<WorkOrderServiceLineInput> serviceLines,
        IReadOnlyList<WorkOrderTaskInput> tasks,
        bool isCanceled,
        DateTimeOffset? cancellationAt,
        ActualTime? actualTime,
        string? remarks = null,
        byte[]? customerSignature = null)
    {
        if (Status != WorkOrderStatus.UnderReview)
            return Error.Conflict("Work order can only be updated while under review.");

        var timeCheck = ValidateActuals(isCanceled, actualTime, cancellationAt);
        if (timeCheck.IsFailure)
            return timeCheck.Error;

        var sync = SyncLineItems(serviceLines, tasks);
        if (sync.IsFailure)
            return sync;

        Customer = customer;
        Station = station;
        OperationType = operationType;
        FlightNumber = flightNumber;
        AircraftType = aircraftType;
        AircraftTailNumber = string.IsNullOrWhiteSpace(aircraftTailNumber) ? null : aircraftTailNumber.Trim().ToUpperInvariant();
        Schedule = schedule;
        TimesActual = actualTime;
        IsCanceled = isCanceled;
        CanceledAt = isCanceled ? cancellationAt : null;
        Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim();
        if (customerSignature is { Length: > 0 })
            CustomerSignature = customerSignature;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Stores the customer signature captured on the mobile app. <c>null</c> clears any
    /// previously-captured signature; a non-empty payload replaces it. Only valid while the
    /// work order is <see cref="WorkOrderStatus.UnderReview"/>.
    /// </summary>
    public Result SetCustomerSignature(byte[]? signature)
    {
        if (Status != WorkOrderStatus.UnderReview)
            return Error.Conflict("Customer signature can only be changed while the work order is under review.");

        CustomerSignature = signature is { Length: > 0 } ? signature : null;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Append-only mutation used by the mobile "return to ramp" flow. Existing rows are
    /// preserved; the supplied lines/tasks are appended with <c>ReturnToRamp = true</c>
    /// (the aggregate enforces it as a defence-in-depth even if the caller forgot). Only
    /// valid while the work order is <see cref="WorkOrderStatus.UnderReview"/>.
    /// </summary>
    public Result AppendReturnToRampLines(
        IReadOnlyList<WorkOrderServiceLineInput> serviceLines,
        IReadOnlyList<WorkOrderTaskInput> tasks,
        byte[]? customerSignature = null)
    {
        if (Status != WorkOrderStatus.UnderReview)
            return Error.Conflict("Return-to-ramp lines can only be added while the work order is under review.");

        if (serviceLines.Count == 0 && tasks.Count == 0)
            return Error.Validation("At least one return-to-ramp line is required.");

        var forcedServices = serviceLines
            .Select(s => new WorkOrderServiceLineInput(s.Service, s.Employee, s.From, s.To, s.Description, ReturnToRamp: true))
            .ToList();
        var forcedTasks = tasks
            .Select(t => new WorkOrderTaskInput(
                t.TaskType, t.Description, t.From, t.To, ReturnToRamp: true,
                t.Employees, t.Tools, t.Materials, t.GeneralSupports, t.Attachments))
            .ToList();

        var built = SyncLineItemsCore(Id, forcedServices, forcedTasks);
        if (built.IsFailure)
            return built.Error;

        _serviceLines.AddRange(built.Value!.ServiceLines);
        _tasks.AddRange(built.Value.Tasks);
        if (customerSignature is { Length: > 0 })
            CustomerSignature = customerSignature;
        Touch();
        return Result.Success();
    }

    public Result SetActualTimes(ActualTime? actualTime, bool isCanceled, DateTimeOffset? cancellationAt)
    {
        if (Status != WorkOrderStatus.UnderReview)
            return Error.Conflict("Work order is not editable.");

        var timeCheck = ValidateActuals(isCanceled, actualTime, cancellationAt);
        if (timeCheck.IsFailure)
            return timeCheck.Error;

        TimesActual = actualTime;
        IsCanceled = isCanceled;
        CanceledAt = isCanceled ? cancellationAt : null;
        Touch();
        return Result.Success();
    }

    public Result SetFlight(OpsFlight.FlightId? flightId)
    {
        if (Status != WorkOrderStatus.UnderReview)
            return Error.Conflict("Flight link can only be changed while under review.");

        FlightId = flightId;
        Touch();
        return Result.Success();
    }

    public Result Approve(WorkOrderNumber workOrderNo, OpsFlight.FlightId? confirmedFlightId, DateTimeOffset now)
    {
        _ = now;
        if (Status != WorkOrderStatus.UnderReview)
            return Error.Conflict("Only a work order under review can be approved.");
        if (!IsCanceled && TimesActual is null)
            return Error.Validation("ATA and ATD are required when the work order is not a cancellation.");

        WorkOrderNo = workOrderNo;
        ConfirmedFlightId = confirmedFlightId;
        Status = WorkOrderStatus.Approved;
        MarkedForDeletionAt = null;
        RaiseDomainEvent(new WorkOrderApprovedEvent(Id));
        Touch();
        return Result.Success();
    }

    public Result Revoke()
    {
        if (Status != WorkOrderStatus.Approved)
            return Error.Conflict("Only an approved work order can be revoked.");

        WorkOrderNo = null;
        ConfirmedFlightId = null;
        Status = WorkOrderStatus.UnderReview;
        MarkedForDeletionAt = null;
        RaiseDomainEvent(new WorkOrderRevokedEvent(Id));
        Touch();
        return Result.Success();
    }

    public Result Reject()
    {
        if (Status != WorkOrderStatus.UnderReview)
            return Error.Conflict("Only a work order under review can be rejected.");

        Status = WorkOrderStatus.Rejected;
        MarkedForDeletionAt = null;
        RaiseDomainEvent(new WorkOrderRejectedEvent(Id));
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Moves an under-review work order to <see cref="WorkOrderStatus.Deleting"/>.
    /// Used when a peer work order on the same flight is approved — the deletion job
    /// hard-deletes the row once the configured delay elapses.
    /// </summary>
    public Result MarkForDeletion(DateTimeOffset markedAt)
    {
        if (Status == WorkOrderStatus.Deleting)
            return Result.Success();
        if (Status != WorkOrderStatus.UnderReview)
            return Error.Conflict("Only an under-review work order can be marked for deletion.");

        Status = WorkOrderStatus.Deleting;
        MarkedForDeletionAt = markedAt;
        RaiseDomainEvent(new WorkOrderMarkedForDeletionEvent(Id, markedAt));
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Cancels a pending deletion and returns the work order to <see cref="WorkOrderStatus.UnderReview"/>.
    /// Triggered when the approval that caused the deletion is revoked before the job runs.
    /// </summary>
    public Result RestoreFromDeletion()
    {
        if (Status == WorkOrderStatus.UnderReview)
            return Result.Success();
        if (Status != WorkOrderStatus.Deleting)
            return Error.Conflict("Only a work order pending deletion can be restored.");

        Status = WorkOrderStatus.UnderReview;
        MarkedForDeletionAt = null;
        RaiseDomainEvent(new WorkOrderRestoredFromDeletionEvent(Id));
        Touch();
        return Result.Success();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    private static Result ValidateActuals(bool isCanceled, ActualTime? actualTime, DateTimeOffset? cancellationAt)
    {
        if (isCanceled && cancellationAt is null)
            return Error.Validation("Cancellation time is required when the work order is a cancellation.");

        _ = actualTime;
        return Result.Success();
    }

    private sealed record BuiltLines(
        List<WorkOrderServiceLine> ServiceLines,
        List<WorkOrderTask> Tasks);

    private static Result<BuiltLines> SyncLineItemsCore(
        WorkOrderId workOrderId,
        IReadOnlyList<WorkOrderServiceLineInput> serviceLines,
        IReadOnlyList<WorkOrderTaskInput> taskInputs)
    {
        var svc = BuildServiceLines(workOrderId, serviceLines);
        if (svc.IsFailure)
            return svc.Error;

        var tasks = new List<WorkOrderTask>(taskInputs.Count);
        foreach (var t in taskInputs)
        {
            var built = WorkOrderTask.Create(
                workOrderId, t.TaskType, t.Description, t.From, t.To, t.ReturnToRamp,
                t.Employees, t.Tools, t.Materials, t.GeneralSupports, t.Attachments);
            if (built.IsFailure) return built.Error;
            tasks.Add(built.Value);
        }

        return new BuiltLines(svc.Value!, tasks);
    }

    private static Result<List<WorkOrderServiceLine>> BuildServiceLines(
        WorkOrderId workOrderId,
        IReadOnlyList<WorkOrderServiceLineInput> source)
    {
        var list = new List<WorkOrderServiceLine>(source.Count);
        foreach (var s in source)
        {
            if (s.To < s.From)
                return Error.Validation("Service line: end time must be on or after start time.");
            list.Add(
                new WorkOrderServiceLine(
                    Guid.NewGuid(),
                    workOrderId,
                    s.Service,
                    s.Employee,
                    s.From,
                    s.To,
                    s.Description,
                    s.ReturnToRamp));
        }

        return list;
    }
}
