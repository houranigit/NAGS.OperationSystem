using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Auditing;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using Operations.Domain.Enumerations;
using Operations.Domain.Events;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.WorkOrders;

public sealed class WorkOrder : AggregateRoot<Guid>, IAuditable
{
    private readonly List<WorkOrderServiceLine> _serviceLines = [];
    private readonly List<WorkOrderTask> _tasks = [];

    private WorkOrder() { }

    string IAuditable.AuditEntityType => "WorkOrder";
    Guid IAuditable.AuditEntityId => Id;

    public Guid FlightId { get; private set; }
    public Guid? MergedIntoWorkOrderId { get; private set; }
    public WorkOrderType Type { get; private set; }
    public WorkOrderStatus Status { get; private set; }
    public bool IsMergeGenerated { get; private set; }

    public Guid OwnerUserId { get; private set; }
    public StaffMemberSnapshot? Owner { get; private set; }

    public CustomerSnapshot Customer { get; private set; } = null!;
    public StationSnapshot Station { get; private set; } = null!;
    public OperationTypeSnapshot OperationType { get; private set; } = null!;
    public FlightNumber PlannedFlightNumber { get; private set; } = null!;
    public ScheduledTime Schedule { get; private set; } = null!;

    public FlightNumber ActualFlightNumber { get; private set; } = null!;
    public AircraftTypeSnapshot? AircraftType { get; private set; }
    public string? AircraftTailNumber { get; private set; }
    public ActualTime? Actuals { get; private set; }
    public CancellationDetails? Cancellation { get; private set; }
    public string? Remarks { get; private set; }
    public string? CustomerSignatureReference { get; private set; }
    public string? CustomerSignatureFileName { get; private set; }
    public string? CustomerSignatureContentType { get; private set; }
    public long? CustomerSignatureSize { get; private set; }
    public DateTimeOffset? CustomerSignedAtUtc { get; private set; }

    public int? ApprovalSequence { get; private set; }
    public string? ApprovalNumber { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<WorkOrderServiceLine> ServiceLines => _serviceLines.AsReadOnly();
    public IReadOnlyList<WorkOrderTask> Tasks => _tasks.AsReadOnly();
    public bool IsEditable => Status is WorkOrderStatus.Submitted or WorkOrderStatus.Returned;

    public static Result<WorkOrder> SubmitNew(
        Flight flight,
        WorkOrderType type,
        Guid ownerUserId,
        StaffMemberSnapshot? owner,
        FlightNumber? actualFlightNumber,
        AircraftTypeSnapshot? aircraftType,
        string? aircraftTailNumber,
        ActualTime? actuals,
        CancellationDetails? cancellation,
        string? remarks,
        IReadOnlyList<WorkOrderServiceLineInput> serviceLines,
        IReadOnlyList<WorkOrderTaskInput> tasks,
        DateTimeOffset now,
        Guid? id = null)
    {
        return SubmitInternal(
            flight,
            type,
            ownerUserId,
            owner,
            actualFlightNumber,
            aircraftType,
            aircraftTailNumber,
            actuals,
            cancellation,
            remarks,
            serviceLines,
            tasks,
            now,
            isMergeGenerated: false,
            id);
    }

    public static Result<WorkOrder> SubmitMerged(
        Flight flight,
        WorkOrderType type,
        Guid ownerUserId,
        StaffMemberSnapshot? owner,
        FlightNumber? actualFlightNumber,
        AircraftTypeSnapshot? aircraftType,
        string? aircraftTailNumber,
        ActualTime? actuals,
        CancellationDetails? cancellation,
        string? remarks,
        IReadOnlyList<WorkOrderServiceLineInput> serviceLines,
        IReadOnlyList<WorkOrderTaskInput> tasks,
        DateTimeOffset now,
        Guid? id = null)
    {
        return SubmitInternal(
            flight,
            type,
            ownerUserId,
            owner,
            actualFlightNumber,
            aircraftType,
            aircraftTailNumber,
            actuals,
            cancellation,
            remarks,
            serviceLines,
            tasks,
            now,
            isMergeGenerated: true,
            id);
    }

    private static Result<WorkOrder> SubmitInternal(
        Flight flight,
        WorkOrderType type,
        Guid ownerUserId,
        StaffMemberSnapshot? owner,
        FlightNumber? actualFlightNumber,
        AircraftTypeSnapshot? aircraftType,
        string? aircraftTailNumber,
        ActualTime? actuals,
        CancellationDetails? cancellation,
        string? remarks,
        IReadOnlyList<WorkOrderServiceLineInput> serviceLines,
        IReadOnlyList<WorkOrderTaskInput> tasks,
        DateTimeOffset now,
        bool isMergeGenerated,
        Guid? id = null)
    {
        if (flight.Status is not (FlightStatus.Scheduled or FlightStatus.InProgress))
            return Error.Conflict("Work orders can only be submitted for scheduled or in-progress flights.", "Operations.WorkOrder.FlightNotOpen");

        var validate = ValidateEditableFields(type, aircraftTailNumber, remarks, actuals, cancellation, serviceLines, tasks);
        if (validate.IsFailure)
            return validate.Error;

        var workOrder = new WorkOrder
        {
            Id = id ?? Guid.NewGuid(),
            FlightId = flight.Id,
            Type = type,
            Status = WorkOrderStatus.Submitted,
            IsMergeGenerated = isMergeGenerated,
            OwnerUserId = ownerUserId,
            Owner = owner,
            Customer = Copy(flight.Customer),
            Station = Copy(flight.Station),
            OperationType = Copy(flight.OperationType),
            PlannedFlightNumber = Copy(flight.FlightNumber),
            Schedule = Copy(flight.Schedule),
            ActualFlightNumber = actualFlightNumber is null ? Copy(flight.FlightNumber) : Copy(actualFlightNumber),
            AircraftType = aircraftType,
            AircraftTailNumber = NormalizeTail(aircraftTailNumber),
            Actuals = actuals,
            Cancellation = type == WorkOrderType.Cancellation ? cancellation : null,
            Remarks = NormalizeRemarks(remarks),
            CreatedAtUtc = now
        };

        workOrder.ReplaceServiceLinesInternal(serviceLines);
        workOrder.ReconcileTasksInternal(tasks);
        workOrder.RaiseDomainEvent(new WorkOrderSubmitted(workOrder.Id, flight.Id));
        return workOrder;
    }

    public Result UpdateDetails(
        WorkOrderType type,
        FlightNumber actualFlightNumber,
        AircraftTypeSnapshot? aircraftType,
        string? aircraftTailNumber,
        ActualTime? actuals,
        CancellationDetails? cancellation,
        string? remarks,
        IReadOnlyList<WorkOrderServiceLineInput> serviceLines,
        IReadOnlyList<WorkOrderTaskInput> tasks,
        DateTimeOffset now)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return editable.Error;

        var validate = ValidateEditableFields(type, aircraftTailNumber, remarks, actuals, cancellation, serviceLines, tasks);
        if (validate.IsFailure)
            return validate.Error;

        var serviceLineIdentities = ValidateServiceLineIdentities(serviceLines);
        if (serviceLineIdentities.IsFailure)
            return serviceLineIdentities.Error;

        var previousType = Type;
        Type = type;
        ActualFlightNumber = actualFlightNumber;
        AircraftType = aircraftType;
        AircraftTailNumber = NormalizeTail(aircraftTailNumber);
        Actuals = actuals;
        Cancellation = type == WorkOrderType.Cancellation ? cancellation : null;
        Remarks = NormalizeRemarks(remarks);
        if (Type == WorkOrderType.Cancellation)
        {
            CustomerSignatureReference = null;
            CustomerSignatureFileName = null;
            CustomerSignatureContentType = null;
            CustomerSignatureSize = null;
            CustomerSignedAtUtc = null;
        }

        var reconcileServiceLines = ReconcileServiceLinesInternal(serviceLines);
        if (reconcileServiceLines.IsFailure)
            return reconcileServiceLines.Error;

        var reconcile = ReconcileTasksInternal(tasks);
        if (reconcile.IsFailure)
            return reconcile.Error;

        UpdatedAtUtc = now;
        RaiseDomainEvent(previousType == Type ? new WorkOrderUpdated(Id) : new WorkOrderConverted(Id));
        return Result.Success();
    }

    public Result ConvertTo(
        WorkOrderType type,
        FlightNumber actualFlightNumber,
        AircraftTypeSnapshot? aircraftType,
        string? aircraftTailNumber,
        ActualTime? actuals,
        CancellationDetails? cancellation,
        string? remarks,
        IReadOnlyList<WorkOrderServiceLineInput> serviceLines,
        IReadOnlyList<WorkOrderTaskInput> tasks,
        DateTimeOffset now) =>
        UpdateDetails(type, actualFlightNumber, aircraftType, aircraftTailNumber, actuals, cancellation, remarks, serviceLines, tasks, now);

    public Result Approve(int sequence, string approvalNumber, Guid approverUserId, DateTimeOffset now)
        => ApproveInternal(sequence, approvalNumber, approverUserId, now, requireCompletionDetails: true);

    /// <summary>
    /// Approves an eligible Per Landing completion produced for review. The application layer must
    /// verify that the flight is Per Landing, In Progress, and has no non-merged work order with
    /// a performed service line.
    /// </summary>
    public Result ApprovePerLandingExtraction(int sequence, string approvalNumber, Guid approverUserId, DateTimeOffset now)
        => ApproveInternal(sequence, approvalNumber, approverUserId, now, requireCompletionDetails: false);

    private Result ApproveInternal(
        int sequence,
        string approvalNumber,
        Guid approverUserId,
        DateTimeOffset now,
        bool requireCompletionDetails)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return editable.Error;
        if (sequence <= 0)
            return Error.Validation("Approval sequence must be greater than zero.", "Operations.WorkOrder.ApprovalSequenceInvalid");
        if (string.IsNullOrWhiteSpace(approvalNumber))
            return Error.Validation("Approval number is required.", "Operations.WorkOrder.ApprovalNumberRequired");

        if (Type == WorkOrderType.Completion && requireCompletionDetails && (Actuals is null || AircraftType is null))
            return Error.Conflict("Completion work orders require actual times and aircraft type before approval.", "Operations.WorkOrder.CompletionApprovalIncomplete");
        if (Type != WorkOrderType.Completion && !requireCompletionDetails)
            return Error.Conflict("Per Landing extraction can approve completion work orders only.", "Operations.PerLanding.CompletionRequired");
        if (Type == WorkOrderType.Cancellation && Cancellation is null)
            return Error.Conflict("Cancellation work orders require cancellation details before approval.", "Operations.WorkOrder.CancellationApprovalIncomplete");

        Status = WorkOrderStatus.Approved;
        ApprovalSequence = sequence;
        ApprovalNumber = approvalNumber.Trim().ToUpperInvariant();
        ApprovedByUserId = approverUserId;
        ApprovedAtUtc = now;
        UpdatedAtUtc = now;
        RaiseDomainEvent(new WorkOrderApproved(Id, ApprovalNumber));
        return Result.Success();
    }

    public Result Return(Guid actorUserId, string reason, DateTimeOffset now)
    {
        if (Status != WorkOrderStatus.Approved)
            return Error.Conflict("Only approved work orders can be returned.", "Operations.WorkOrder.NotApproved");

        if (string.IsNullOrWhiteSpace(reason))
            return Error.Validation("Return reason is required.", "Operations.WorkOrder.ReturnReasonRequired");
        if (reason.Trim().Length > 1000)
            return Error.Validation("Return reason must be at most 1000 characters.", "Operations.WorkOrder.ReturnReasonTooLong");

        Status = WorkOrderStatus.Returned;
        ApprovalSequence = null;
        ApprovalNumber = null;
        ApprovedByUserId = null;
        ApprovedAtUtc = null;
        UpdatedAtUtc = now;
        _ = actorUserId;
        RaiseDomainEvent(new WorkOrderReturned(Id));
        return Result.Success();
    }

    public Result MarkMergedInto(Guid generatedWorkOrderId, DateTimeOffset now)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return editable.Error;

        Status = WorkOrderStatus.Merged;
        MergedIntoWorkOrderId = generatedWorkOrderId;
        ApprovalSequence = null;
        ApprovalNumber = null;
        ApprovedByUserId = null;
        ApprovedAtUtc = null;
        UpdatedAtUtc = now;
        RaiseDomainEvent(new WorkOrderMerged(Id, generatedWorkOrderId));
        return Result.Success();
    }

    public Result<WorkOrderTaskAttachment> AddTaskAttachment(
        Guid taskId,
        TaskAttachmentKind kind,
        string storageReference,
        string originalFileName,
        string contentType,
        long size,
        DateTimeOffset now)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return editable.Error;

        var task = _tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is null)
            return Error.NotFound("Task not found.", "Operations.WorkOrder.TaskNotFound");

        var attachment = task.AddAttachment(kind, storageReference, originalFileName, contentType, size);
        if (attachment.IsFailure)
            return attachment.Error;

        UpdatedAtUtc = now;
        RaiseDomainEvent(new WorkOrderUpdated(Id));
        return attachment;
    }

    public Result<string> RemoveTaskAttachment(Guid taskId, Guid attachmentId, DateTimeOffset now)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return editable.Error;

        var task = _tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is null)
            return Error.NotFound("Task not found.", "Operations.WorkOrder.TaskNotFound");

        var storageReference = task.RemoveAttachment(attachmentId);
        if (storageReference.IsFailure)
            return storageReference.Error;

        UpdatedAtUtc = now;
        RaiseDomainEvent(new WorkOrderUpdated(Id));
        return storageReference;
    }

    public Result<WorkOrderServiceLineAttachment> AddServiceLineAttachment(
        Guid serviceLineId,
        TaskAttachmentKind kind,
        string storageReference,
        string originalFileName,
        string contentType,
        long size,
        DateTimeOffset now)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return editable.Error;

        var serviceLine = _serviceLines.FirstOrDefault(line => line.Id == serviceLineId);
        if (serviceLine is null)
            return Error.NotFound("Service line not found.", "Operations.WorkOrder.ServiceLineNotFound");

        var attachment = serviceLine.AddAttachment(kind, storageReference, originalFileName, contentType, size);
        if (attachment.IsFailure)
            return attachment.Error;

        UpdatedAtUtc = now;
        RaiseDomainEvent(new WorkOrderUpdated(Id));
        return attachment;
    }

    public Result<string> RemoveServiceLineAttachment(Guid serviceLineId, Guid attachmentId, DateTimeOffset now)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return editable.Error;

        var serviceLine = _serviceLines.FirstOrDefault(line => line.Id == serviceLineId);
        if (serviceLine is null)
            return Error.NotFound("Service line not found.", "Operations.WorkOrder.ServiceLineNotFound");

        var storageReference = serviceLine.RemoveAttachment(attachmentId);
        if (storageReference.IsFailure)
            return storageReference.Error;

        UpdatedAtUtc = now;
        RaiseDomainEvent(new WorkOrderUpdated(Id));
        return storageReference;
    }

    public Result SetCustomerSignature(
        string storageReference,
        string fileName,
        string contentType,
        long size,
        DateTimeOffset now)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return editable.Error;
        if (Type != WorkOrderType.Completion)
            return Error.Conflict("Customer signatures are only available for completion work orders.", "Operations.WorkOrder.SignatureTypeInvalid");
        if (string.IsNullOrWhiteSpace(storageReference))
            return Error.Validation("Signature storage reference is required.", "Operations.WorkOrder.SignatureStorageRequired");
        if (string.IsNullOrWhiteSpace(fileName))
            return Error.Validation("Signature file name is required.", "Operations.WorkOrder.SignatureFileNameRequired");
        if (string.IsNullOrWhiteSpace(contentType))
            return Error.Validation("Signature content type is required.", "Operations.WorkOrder.SignatureContentTypeRequired");
        if (size <= 0)
            return Error.Validation("Signature file is empty.", "Operations.WorkOrder.SignatureEmpty");

        CustomerSignatureReference = storageReference.Trim();
        CustomerSignatureFileName = TrimFileName(fileName);
        CustomerSignatureContentType = contentType.Trim();
        CustomerSignatureSize = size;
        CustomerSignedAtUtc = now;
        UpdatedAtUtc = now;
        RaiseDomainEvent(new WorkOrderUpdated(Id));
        return Result.Success();
    }

    public Result<string?> RemoveCustomerSignature(DateTimeOffset now)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return editable.Error;

        var storageReference = CustomerSignatureReference;
        CustomerSignatureReference = null;
        CustomerSignatureFileName = null;
        CustomerSignatureContentType = null;
        CustomerSignatureSize = null;
        CustomerSignedAtUtc = null;
        UpdatedAtUtc = now;
        RaiseDomainEvent(new WorkOrderUpdated(Id));
        return storageReference;
    }

    public Result EnsureEditable() =>
        IsEditable
            ? Result.Success()
            : Error.Conflict("This work order is locked and can no longer be edited.", "Operations.WorkOrder.Locked");

    private void ReplaceServiceLinesInternal(IReadOnlyList<WorkOrderServiceLineInput> serviceLines)
    {
        _serviceLines.Clear();
        foreach (var line in serviceLines)
            _serviceLines.Add(new WorkOrderServiceLine(Guid.NewGuid(), Id, line));
    }

    private Result ReconcileServiceLinesInternal(IReadOnlyList<WorkOrderServiceLineInput> serviceLines)
    {
        var identities = ValidateServiceLineIdentities(serviceLines);
        if (identities.IsFailure)
            return identities.Error;

        var existingById = _serviceLines.ToDictionary(line => line.Id);
        var retained = new HashSet<Guid>();

        foreach (var input in serviceLines)
        {
            if (input.Id is { } serviceLineId)
            {
                var existing = existingById[serviceLineId];
                existing.Update(input);
                retained.Add(serviceLineId);
                continue;
            }

            var added = new WorkOrderServiceLine(Guid.NewGuid(), Id, input);
            _serviceLines.Add(added);
            retained.Add(added.Id);
        }

        _serviceLines.RemoveAll(line => !retained.Contains(line.Id));
        return Result.Success();
    }

    private Result ValidateServiceLineIdentities(IReadOnlyList<WorkOrderServiceLineInput> serviceLines)
    {
        var incomingIds = serviceLines.Where(line => line.Id.HasValue).Select(line => line.Id!.Value).ToList();
        if (incomingIds.Count != incomingIds.Distinct().Count())
            return Error.Validation("Service line ids must be unique.", "Operations.WorkOrder.ServiceLineIdsDuplicate");

        var existingIds = _serviceLines.Select(line => line.Id).ToHashSet();
        if (incomingIds.Any(id => !existingIds.Contains(id)))
            return Error.Conflict("One or more service line ids do not belong to this work order.", "Operations.WorkOrder.ServiceLineIdForeign");

        return Result.Success();
    }

    private Result ReconcileTasksInternal(IReadOnlyList<WorkOrderTaskInput> tasks)
    {
        var incomingIds = tasks.Where(t => t.Id.HasValue).Select(t => t.Id!.Value).ToList();
        if (incomingIds.Count != incomingIds.Distinct().Count())
            return Error.Validation("Task ids must be unique.", "Operations.WorkOrder.TaskIdsDuplicate");

        var existingById = _tasks.ToDictionary(t => t.Id);
        var retained = new HashSet<Guid>();

        foreach (var input in tasks)
        {
            if (input.Id is { } taskId)
            {
                if (!existingById.TryGetValue(taskId, out var existing))
                    return Error.Conflict("One or more task ids do not belong to this work order.", "Operations.WorkOrder.TaskIdForeign");

                existing.Update(input);
                retained.Add(taskId);
                continue;
            }

            var added = new WorkOrderTask(Guid.NewGuid(), Id, input);
            _tasks.Add(added);
            retained.Add(added.Id);
        }

        _tasks.RemoveAll(t => !retained.Contains(t.Id));
        return Result.Success();
    }

    private static Result ValidateEditableFields(
        WorkOrderType type,
        string? aircraftTailNumber,
        string? remarks,
        ActualTime? actuals,
        CancellationDetails? cancellation,
        IReadOnlyList<WorkOrderServiceLineInput> serviceLines,
        IReadOnlyList<WorkOrderTaskInput> tasks)
    {
        if (NormalizeTail(aircraftTailNumber)?.Length > 20)
            return Error.Validation("Aircraft tail number must be at most 20 characters.", "Operations.WorkOrder.TailTooLong");
        if (NormalizeRemarks(remarks)?.Length > 2000)
            return Error.Validation("Remarks must be at most 2000 characters.", "Operations.WorkOrder.RemarksTooLong");
        if (type == WorkOrderType.Cancellation && cancellation is null)
            return Error.Validation("Cancellation details are required.", "Operations.WorkOrder.CancellationRequired");
        if (type == WorkOrderType.Completion && cancellation is not null)
            return Error.Validation("Completion work orders cannot include cancellation details.", "Operations.WorkOrder.CancellationNotAllowed");

        foreach (var line in serviceLines)
        {
            if (line.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService)
                return Error.Validation("Aircraft Per Landing cannot be selected as a work order service line.", "Operations.WorkOrder.PerLandingLineNotAllowed");
            if (line.PerformedBy is not { Count: > 0 })
                return Error.Validation("Every service line requires at least one performer.", "Operations.WorkOrder.ServiceLinePerformerRequired");
            if (string.IsNullOrWhiteSpace(line.Description) is false && line.Description.Trim().Length > 2000)
                return Error.Validation("Service line description must be at most 2000 characters.", "Operations.WorkOrder.ServiceLineDescriptionTooLong");
        }

        foreach (var task in tasks)
        {
            if (string.IsNullOrWhiteSpace(task.Description) is false && task.Description.Trim().Length > 2000)
                return Error.Validation("Task description must be at most 2000 characters.", "Operations.WorkOrder.TaskDescriptionTooLong");
        }

        _ = actuals;
        return Result.Success();
    }

    private static string? NormalizeTail(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string? NormalizeRemarks(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string TrimFileName(string fileName)
    {
        var trimmed = Path.GetFileName(fileName.Trim());
        return trimmed.Length <= 255 ? trimmed : trimmed[..255];
    }

    private static CustomerSnapshot Copy(CustomerSnapshot value) =>
        new(value.CustomerId, value.IataCode, value.Name);

    private static StationSnapshot Copy(StationSnapshot value) =>
        new(value.StationId, value.IataCode, value.Name);

    private static OperationTypeSnapshot Copy(OperationTypeSnapshot value) =>
        new(value.OperationTypeId, value.Name);

    private static FlightNumber Copy(FlightNumber value) =>
        FlightNumber.Create(value.Value).Value;

    private static ScheduledTime Copy(ScheduledTime value) =>
        ScheduledTime.Create(value.Sta, value.Std).Value;
}
