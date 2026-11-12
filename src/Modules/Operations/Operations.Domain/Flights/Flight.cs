using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Auditing;
using BuildingBlocks.Domain.Results;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

/// <summary>A flight with planning data and employee-owned operational or cancellation submissions.</summary>
public sealed class Flight : AggregateRoot<Guid>, IAuditable
{
    private readonly List<PlannedService> _plannedServices = [];
    private readonly List<FlightAssignedEmployee> _assignedEmployees = [];
    private readonly List<FlightArrivalSubmission> _arrivalSubmissions = [];
    private readonly List<FlightDepartureSubmission> _departureSubmissions = [];
    private readonly List<FlightCancellationSubmission> _cancellationSubmissions = [];
    private readonly List<FlightService> _services = [];
    private readonly List<FlightTask> _tasks = [];
    private readonly List<FlightReturnToRamp> _returnToRamps = [];

    private Flight() { }

    string IAuditable.AuditEntityType => "Flight";
    Guid IAuditable.AuditEntityId => Id;

    public CustomerSnapshot Customer { get; private set; } = null!;
    public StationSnapshot Station { get; private set; } = null!;
    public OperationTypeSnapshot OperationType { get; private set; } = null!;
    public FlightNumber FlightNumber { get; private set; } = null!;
    public ScheduledTime Schedule { get; private set; } = null!;
    public AircraftTypeSnapshot? AircraftType { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<PlannedService> PlannedServices => _plannedServices.AsReadOnly();
    public IReadOnlyList<FlightAssignedEmployee> AssignedEmployees => _assignedEmployees.AsReadOnly();
    public IReadOnlyList<FlightArrivalSubmission> ArrivalSubmissions => _arrivalSubmissions.AsReadOnly();
    public IReadOnlyList<FlightDepartureSubmission> DepartureSubmissions => _departureSubmissions.AsReadOnly();
    public IReadOnlyList<FlightCancellationSubmission> CancellationSubmissions => _cancellationSubmissions.AsReadOnly();
    public IReadOnlyList<FlightService> Services => _services.Where(item => item.ReturnToRampId is null).ToList().AsReadOnly();
    public IReadOnlyList<FlightTask> Tasks => _tasks.Where(item => item.ReturnToRampId is null).ToList().AsReadOnly();
    public IReadOnlyList<FlightService> ServiceRecords => _services.AsReadOnly();
    public IReadOnlyList<FlightTask> TaskRecords => _tasks.AsReadOnly();
    public IReadOnlyList<FlightReturnToRamp> ReturnToRamps => _returnToRamps.AsReadOnly();
    public int LastReturnToRampSequence { get; private set; }
    public bool IsPerLanding => _plannedServices.Any(service => service.IsAircraftPerLanding);

    public static Result<Flight> ScheduleNew(
        CustomerSnapshot customer,
        StationSnapshot station,
        OperationTypeSnapshot operationType,
        FlightNumber flightNumber,
        ScheduledTime schedule,
        AircraftTypeSnapshot? aircraftType,
        IReadOnlyList<ServiceSnapshot> plannedServices,
        IReadOnlyList<StaffMemberSnapshot> assignedEmployees,
        Guid createdByUserId,
        DateTimeOffset now,
        Guid? id = null)
    {
        var numberCheck = ValidateCustomerFlightNumber(customer, flightNumber);
        if (numberCheck.IsFailure)
            return numberCheck.Error;

        var servicesCheck = PerLandingPolicy.ValidatePlannedServices(plannedServices);
        if (servicesCheck.IsFailure)
            return servicesCheck.Error;
        if (plannedServices.Any(service => PerLandingPolicy.IsAircraftPerLanding(service.ServiceId)) && assignedEmployees.Count > 0)
            return PerLandingAssignmentError();

        var flight = new Flight
        {
            Id = id ?? Guid.NewGuid(),
            Customer = customer,
            Station = station,
            OperationType = operationType,
            FlightNumber = flightNumber,
            Schedule = schedule,
            AircraftType = aircraftType,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = now
        };
        flight.SetPlannedServices(plannedServices);
        flight.SetAssignedEmployees(assignedEmployees);
        return flight;
    }

    public Result ChangeFlightNumber(FlightNumber newNumber, DateTimeOffset now)
    {
        var numberCheck = ValidateCustomerFlightNumber(Customer, newNumber);
        if (numberCheck.IsFailure)
            return numberCheck.Error;

        if (FlightNumber == newNumber)
            return Result.Success();
        FlightNumber = newNumber;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result UpdatePlanning(
        ScheduledTime schedule,
        AircraftTypeSnapshot? aircraftType,
        IReadOnlyList<ServiceSnapshot> plannedServices,
        IReadOnlyList<StaffMemberSnapshot>? assignedEmployees,
        DateTimeOffset now)
    {
        var check = PerLandingPolicy.ValidatePlannedServices(plannedServices);
        if (check.IsFailure)
            return check.Error;
        var employeeCount = assignedEmployees?.Count ?? _assignedEmployees.Count;
        if (plannedServices.Any(service => PerLandingPolicy.IsAircraftPerLanding(service.ServiceId)) && employeeCount > 0)
            return PerLandingAssignmentError();

        Schedule = schedule;
        AircraftType = aircraftType;
        SetPlannedServices(plannedServices);
        if (assignedEmployees is not null)
            SetAssignedEmployees(assignedEmployees);
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result ReplaceAssignedEmployees(IReadOnlyList<StaffMemberSnapshot> employees, DateTimeOffset now)
    {
        if (IsPerLanding && employees.Count > 0)
            return PerLandingAssignmentError();
        SetAssignedEmployees(employees);
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result InviteEmployees(IReadOnlyList<StaffMemberSnapshot> employees, DateTimeOffset now)
    {
        if (IsPerLanding)
            return PerLandingAssignmentError();

        foreach (var employee in employees)
        {
            if (_assignedEmployees.Any(assigned => assigned.Employee.StaffMemberId == employee.StaffMemberId))
                continue;
            _assignedEmployees.Add(new FlightAssignedEmployee(Guid.NewGuid(), Id, employee));
            UpdatedAtUtc = now;
        }

        return Result.Success();
    }

    /// <summary>Records or updates one employee's cancellation. Existing RTRs, tasks, services, and submission collections must be loaded.</summary>
    public Result<FlightCancellationSubmission> SubmitCancellation(
        StaffMemberSnapshot? submittedByEmployee,
        CancellationDetails? details,
        Guid submittedByUserId,
        DateTimeOffset now)
    {
        if (submittedByEmployee is null || submittedByEmployee.StaffMemberId == Guid.Empty)
            return Error.Validation("Submitting employee is required.", "Operations.CancellationSubmission.SubmittedByEmployeeRequired");
        if (submittedByUserId == Guid.Empty)
            return Error.Validation("Submitting user is required.", "Operations.CancellationSubmission.SubmittedByUserRequired");
        if (details is null)
            return Error.Validation("Cancellation details are required.", "Operations.CancellationSubmission.DetailsRequired");
        if (_arrivalSubmissions.Any(item => item.SubmittedByEmployee.StaffMemberId == submittedByEmployee.StaffMemberId) ||
            _departureSubmissions.Any(item => item.SubmittedByEmployee.StaffMemberId == submittedByEmployee.StaffMemberId) ||
            _services.Count > 0 || _tasks.Count > 0 || _returnToRamps.Count > 0)
            return Error.Conflict("An employee with operational submissions cannot submit cancellation. Existing performed work also prevents cancellation.", "Operations.CancellationSubmission.OperationsAlreadyRecorded");

        var submission = _cancellationSubmissions.SingleOrDefault(item => item.SubmittedByEmployee.StaffMemberId == submittedByEmployee.StaffMemberId);
        if (submission is not null)
        {
            if (submission.Details == details)
                return submission;

            submission.UpdateDetails(details, submittedByUserId, now.ToUniversalTime());
        }
        else
        {
            var isAccepted = _cancellationSubmissions.Count == 0;
            foreach (var existing in _cancellationSubmissions)
                existing.ClearAcceptance();

            submission = new FlightCancellationSubmission(Guid.NewGuid(), Id, submittedByEmployee, details,
                isAccepted, submittedByUserId, now.ToUniversalTime());
            _cancellationSubmissions.Add(submission);
        }

        UpdatedAtUtc = now.ToUniversalTime();
        return submission;
    }

    /// <summary>Removes only the caller's identified cancellation. Existing cancellation submissions must be loaded.</summary>
    public Result RevertCancellation(Guid staffMemberId, Guid submissionId, DateTimeOffset now)
    {
        if (staffMemberId == Guid.Empty || submissionId == Guid.Empty)
            return Error.Validation("The employee and cancellation submission are required.", "Operations.CancellationSubmission.IdentityRequired");
        var own = _cancellationSubmissions.SingleOrDefault(item => item.SubmittedByEmployee.StaffMemberId == staffMemberId);
        if (_cancellationSubmissions.Any(item => item.Id == submissionId && item.SubmittedByEmployee.StaffMemberId != staffMemberId))
            return Error.Forbidden("Only the submitting employee can revert this cancellation.", "Operations.Cancellation.RevertForbidden");
        if (own is null) return Result.Success();
        if (own.Id != submissionId)
            return Error.Conflict("Your cancellation changed. Reload the flight before reverting it.", "Operations.Cancellation.SubmissionChanged");
        _cancellationSubmissions.Remove(own);
        // Do not select another employee's previously contested input as an approval side effect.
        UpdatedAtUtc = now.ToUniversalTime();
        return Result.Success();
    }

    /// <summary>Records or updates one employee's arrival. Existing submission collections must be loaded.</summary>
    public Result<FlightArrivalSubmission> SubmitArrival(
        StaffMemberSnapshot? submittedByEmployee,
        ArrivalDetails? details,
        Guid submittedByUserId,
        DateTimeOffset now)
    {
        if (submittedByEmployee is null || submittedByEmployee.StaffMemberId == Guid.Empty)
            return Error.Validation("Submitting employee is required.", "Operations.ArrivalSubmission.SubmittedByEmployeeRequired");
        if (submittedByUserId == Guid.Empty)
            return Error.Validation("Submitting user is required.", "Operations.ArrivalSubmission.SubmittedByUserRequired");
        if (details is null)
            return Error.Validation("Arrival details are required.", "Operations.ArrivalSubmission.DetailsRequired");
        if (_cancellationSubmissions.Any(item => item.SubmittedByEmployee.StaffMemberId == submittedByEmployee.StaffMemberId))
            return Error.Conflict("Revert your cancellation before submitting arrival details.", "Operations.Flight.CancellationSubmitted");

        var submission = _arrivalSubmissions.SingleOrDefault(item => item.SubmittedByEmployee.StaffMemberId == submittedByEmployee.StaffMemberId);
        if (submission is not null)
        {
            if (submission.Details == details)
                return submission;

            submission.UpdateDetails(details, submittedByUserId, now.ToUniversalTime());
        }
        else
        {
            var isAccepted = _arrivalSubmissions.Count == 0;
            foreach (var existing in _arrivalSubmissions)
                existing.ClearAcceptance();

            submission = new FlightArrivalSubmission(Guid.NewGuid(), Id, submittedByEmployee, details,
                isAccepted, submittedByUserId, now.ToUniversalTime());
            _arrivalSubmissions.Add(submission);
        }

        UpdatedAtUtc = now.ToUniversalTime();
        return submission;
    }

    /// <summary>Records or updates one employee's departure. Existing submission collections must be loaded.</summary>
    public Result<FlightDepartureSubmission> SubmitDeparture(
        StaffMemberSnapshot? submittedByEmployee,
        DepartureDetails? details,
        Guid submittedByUserId,
        DateTimeOffset now)
    {
        if (submittedByEmployee is null || submittedByEmployee.StaffMemberId == Guid.Empty)
            return Error.Validation("Submitting employee is required.", "Operations.DepartureSubmission.SubmittedByEmployeeRequired");
        if (submittedByUserId == Guid.Empty)
            return Error.Validation("Submitting user is required.", "Operations.DepartureSubmission.SubmittedByUserRequired");
        if (details is null)
            return Error.Validation("Departure details are required.", "Operations.DepartureSubmission.DetailsRequired");
        if (_cancellationSubmissions.Any(item => item.SubmittedByEmployee.StaffMemberId == submittedByEmployee.StaffMemberId))
            return Error.Conflict("Revert your cancellation before submitting departure details.", "Operations.Flight.CancellationSubmitted");

        var submission = _departureSubmissions.SingleOrDefault(item => item.SubmittedByEmployee.StaffMemberId == submittedByEmployee.StaffMemberId);
        if (submission is not null)
        {
            if (submission.Details == details)
                return submission;

            submission.UpdateDetails(details, submittedByUserId, now.ToUniversalTime());
        }
        else
        {
            var isAccepted = _departureSubmissions.Count == 0;
            foreach (var existing in _departureSubmissions)
                existing.ClearAcceptance();

            submission = new FlightDepartureSubmission(Guid.NewGuid(), Id, submittedByEmployee, details,
                isAccepted, submittedByUserId, now.ToUniversalTime());
            _departureSubmissions.Add(submission);
        }

        UpdatedAtUtc = now.ToUniversalTime();
        return submission;
    }

    /// <summary>Records a performed service. Existing services and cancellation submissions must be loaded.</summary>
    public Result<FlightService> AddService(ServiceSnapshot? service, string? customerReferenceNumber,
        TimeWindow? window, string? notes, Guid userId, DateTimeOffset now, Guid? returnToRampId = null,
        IReadOnlyList<FlightServicePerformerInput>? initialPerformers = null, Guid? id = null)
    {
        if (id == Guid.Empty)
            return Error.Validation("Service record ID is required.", "Operations.FlightService.IdRequired");
        if (id.HasValue && _services.Any(item => item.Id == id.Value))
            return Error.Conflict("Service record ID is already in use.", "Operations.FlightService.IdConflict");
        var check = ValidateServiceWrite(userId, returnToRampId);
        if (check.IsFailure)
            return check.Error;
        var created = FlightService.Create(Id, service, customerReferenceNumber, window, notes, userId, now.ToUniversalTime(), returnToRampId, id);
        if (created.IsFailure)
            return created.Error;

        var record = created.Value;
        foreach (var employee in initialPerformers ?? [])
        {
            if (employee.Id.HasValue && _services.Any(item => item.Performers.Any(performer => performer.Id == employee.Id.Value)))
                return Error.Conflict("Performer record ID is already in use.", "Operations.FlightService.PerformerIdConflict");
            var added = record.AddPerformer(employee.Employee, employee.Window, employee.Id);
            if (added.IsFailure)
                return added.Error;
        }
        var occurrenceCheck = ValidateReturnToRampService(returnToRampId, record.Window, record.Performers.Select(item => item.Window));
        if (occurrenceCheck.IsFailure)
            return occurrenceCheck.Error;
        _services.Add(record);
        if (returnToRampId is { } occurrenceId)
        {
            var occurrence = _returnToRamps.Single(item => item.Id == occurrenceId);
            occurrence.AddService(record);
            occurrence.MarkUpdated(userId, now.ToUniversalTime());
        }
        UpdatedAtUtc = now.ToUniversalTime();
        return created.Value;
    }

    /// <summary>Atomically saves one normal service and its complete performer list under stable client IDs.</summary>
    public Result<FlightService> SaveService(Guid recordId, ServiceSnapshot? service, string? customerReferenceNumber,
        TimeWindow? window, string? notes, IReadOnlyList<FlightServicePerformerInput> performers, Guid userId, DateTimeOffset now)
    {
        var check = ValidateServiceWrite(userId, null);
        if (check.IsFailure) return check.Error;
        var existing = _services.SingleOrDefault(item => item.Id == recordId);
        if (existing?.ReturnToRampId is not null)
            return Error.NotFound("Performed service was not found on this flight.", "Operations.FlightService.NotFound");
        if (existing is not null && service is not null && existing.Service.ServiceId != service.ServiceId && existing.HasOtherUserEnd)
            return FlightService.SelectionReadOnly();
        var created = FlightService.Create(Id, service, customerReferenceNumber, window, notes, userId, now.ToUniversalTime(), id: recordId);
        if (created.IsFailure) return created.Error;
        var replacement = created.Value;
        foreach (var performer in performers)
        {
            if (performer.Id.HasValue && _services.Any(item => item.Id != recordId && item.Performers.Any(child => child.Id == performer.Id.Value)))
                return Error.Conflict("Performer record ID is already in use.", "Operations.FlightService.PerformerIdConflict");
            var added = replacement.AddPerformer(performer.Employee, performer.Window, performer.Id);
            if (added.IsFailure) return added.Error;
        }
        if (existing is null)
        {
            _services.Add(replacement);
            UpdatedAtUtc = now.ToUniversalTime();
            return replacement;
        }
        if (existing.ReplaceDetailsAndPerformers(replacement, userId)) MarkServiceUpdated(existing, userId, now);
        return existing;
    }

    public Result<FlightService> UpdateService(Guid serviceId, ServiceSnapshot? service, string? customerReferenceNumber,
        TimeWindow? window, string? notes, Guid userId, DateTimeOffset now, Guid? returnToRampId = null)
    {
        var found = FindServiceForWrite(serviceId, userId, returnToRampId);
        if (found.IsFailure)
            return found.Error;
        var occurrenceCheck = ValidateReturnToRampService(returnToRampId, window, found.Value.Performers.Select(item => item.Window));
        if (occurrenceCheck.IsFailure)
            return occurrenceCheck.Error;
        var changed = found.Value.UpdateDetails(service, customerReferenceNumber, window, notes, userId);
        if (changed.IsFailure)
            return changed.Error;
        if (changed.Value)
            MarkServiceUpdated(found.Value, userId, now);
        return found.Value;
    }

    public Result<FlightServicePerformer> AddServicePerformer(Guid serviceId, StaffMemberSnapshot? employee,
        TimeWindow? window, Guid userId, DateTimeOffset now, Guid? returnToRampId = null, Guid? id = null)
    {
        if (id.HasValue && _services.Any(item => item.Performers.Any(performer => performer.Id == id.Value)))
            return Error.Conflict("Performer record ID is already in use.", "Operations.FlightService.PerformerIdConflict");
        var found = FindServiceForWrite(serviceId, userId, returnToRampId);
        if (found.IsFailure)
            return found.Error;
        var periodCheck = ValidateReturnToRampEmployee(returnToRampId, window);
        if (periodCheck.IsFailure)
            return periodCheck.Error;
        var added = found.Value.AddPerformer(employee, window, id);
        if (added.IsFailure)
            return added.Error;

        MarkServiceUpdated(found.Value, userId, now);
        return added.Value;
    }

    public Result<FlightServicePerformer> UpdateServicePerformer(Guid serviceId, Guid performerId,
        StaffMemberSnapshot? employee, TimeWindow? window, Guid userId, DateTimeOffset now, Guid? returnToRampId = null)
    {
        var found = FindServiceForWrite(serviceId, userId, returnToRampId);
        if (found.IsFailure)
            return found.Error;
        var periodCheck = ValidateReturnToRampEmployee(returnToRampId, window);
        if (periodCheck.IsFailure)
            return periodCheck.Error;
        var changed = found.Value.UpdatePerformer(performerId, employee, window);
        if (changed.IsFailure)
            return changed.Error;
        if (changed.Value)
            MarkServiceUpdated(found.Value, userId, now);
        return found.Value.Performers.Single(performer => performer.Id == performerId);
    }

    public Result RemoveServicePerformer(Guid serviceId, Guid performerId, Guid userId, DateTimeOffset now, Guid? returnToRampId = null)
    {
        var found = FindServiceForWrite(serviceId, userId, returnToRampId);
        if (found.IsFailure)
            return found.Error;
        if (found.Value.Performers.Any(item => item.Id == performerId))
        {
            var periodCheck = ValidateReturnToRampPerformerRemoval(returnToRampId, found.Value.Performers.Count, "Service");
            if (periodCheck.IsFailure)
                return periodCheck;
        }
        var removed = found.Value.RemovePerformer(performerId);
        if (removed.IsFailure)
            return removed;

        MarkServiceUpdated(found.Value, userId, now);
        return Result.Success();
    }

    public Result<FlightServiceAttachment> AddServiceAttachment(Guid serviceId, AttachmentDetails? details,
        Guid userId, DateTimeOffset now, Guid? returnToRampId = null, Guid? id = null)
    {
        if (id.HasValue && _services.Any(item => item.Attachments.Any(attachment => attachment.Id == id.Value)))
            return Error.Conflict("Attachment record ID is already in use.", "Operations.FlightService.AttachmentIdConflict");
        var found = FindServiceForWrite(serviceId, userId, returnToRampId);
        if (found.IsFailure)
            return found.Error;
        var added = found.Value.AddAttachment(details, userId, now.ToUniversalTime(), id);
        if (added.IsFailure)
            return added.Error;

        MarkServiceUpdated(found.Value, userId, now);
        return added.Value;
    }

    public Result<FlightServiceAttachment> RemoveServiceAttachment(Guid serviceId, Guid attachmentId,
        Guid userId, DateTimeOffset now, Guid? returnToRampId = null)
    {
        var found = FindServiceForWrite(serviceId, userId, returnToRampId);
        if (found.IsFailure)
            return found.Error;
        var removed = found.Value.RemoveAttachment(attachmentId);
        if (removed.IsFailure)
            return removed.Error;

        MarkServiceUpdated(found.Value, userId, now);
        return removed.Value;
    }

    private Result ValidateServiceWrite(Guid userId, Guid? returnToRampId)
    {
        if (userId == Guid.Empty)
            return Error.Validation("Recording user is required.", "Operations.FlightService.UserRequired");
        if (_cancellationSubmissions.Count > 0)
            return Error.Conflict("A flight with cancellation submissions cannot have performed services.", "Operations.Flight.CancellationSubmitted");
        return ValidateReturnToRampScope(returnToRampId);
    }

    private Result<FlightService> FindServiceForWrite(Guid serviceId, Guid userId, Guid? returnToRampId)
    {
        var check = ValidateServiceWrite(userId, returnToRampId);
        if (check.IsFailure)
            return check.Error;
        var service = _services.SingleOrDefault(item => item.Id == serviceId && item.ReturnToRampId == returnToRampId);
        return service is null
            ? Error.NotFound("Performed service was not found on this flight.", "Operations.FlightService.NotFound")
            : service;
    }

    private void MarkServiceUpdated(FlightService service, Guid userId, DateTimeOffset now)
    {
        service.MarkUpdated(userId, now.ToUniversalTime());
        MarkReturnToRampUpdated(service.ReturnToRampId, userId, now);
        UpdatedAtUtc = now.ToUniversalTime();
    }

    /// <summary>Records a task. Existing tasks and cancellation submissions must be loaded.</summary>
    public Result<FlightTask> AddTask(FlightTaskType taskType, AtaChapterSnapshot? ataChapter,
        string? customerReferenceNumber, TimeWindow? window, string? notes, Guid userId, DateTimeOffset now, Guid? returnToRampId = null,
        IReadOnlyList<FlightTaskPerformerInput>? initialPerformers = null)
    {
        var check = ValidateTaskWrite(userId, returnToRampId);
        if (check.IsFailure)
            return check.Error;
        var created = FlightTask.Create(Id, taskType, ataChapter, customerReferenceNumber, window, notes, userId, now.ToUniversalTime(), returnToRampId);
        if (created.IsFailure)
            return created.Error;

        var record = created.Value;
        foreach (var employee in initialPerformers ?? [])
        {
            var added = record.AddPerformer(employee.Employee, employee.Window, employee.Notes);
            if (added.IsFailure)
                return added.Error;
        }
        var occurrenceCheck = ValidateReturnToRampTask(returnToRampId, record.Window, record.Performers.Select(item => item.Window));
        if (occurrenceCheck.IsFailure)
            return occurrenceCheck.Error;
        _tasks.Add(record);
        if (returnToRampId is { } occurrenceId)
        {
            var occurrence = _returnToRamps.Single(item => item.Id == occurrenceId);
            occurrence.AddTask(record);
            occurrence.MarkUpdated(userId, now.ToUniversalTime());
        }
        UpdatedAtUtc = now.ToUniversalTime();
        return created.Value;
    }

    public Result<FlightTask> UpdateTask(Guid taskId, FlightTaskType taskType, AtaChapterSnapshot? ataChapter,
        string? customerReferenceNumber, TimeWindow? window, string? notes, Guid userId, DateTimeOffset now, Guid? returnToRampId = null)
    {
        var found = FindTaskForWrite(taskId, userId, returnToRampId);
        if (found.IsFailure)
            return found.Error;
        var occurrenceCheck = ValidateReturnToRampTask(returnToRampId, window, found.Value.Performers.Select(item => item.Window));
        if (occurrenceCheck.IsFailure)
            return occurrenceCheck.Error;
        var changed = found.Value.UpdateDetails(taskType, ataChapter, customerReferenceNumber, window, notes, userId);
        if (changed.IsFailure)
            return changed.Error;
        if (changed.Value)
            MarkTaskUpdated(found.Value, userId, now);
        return found.Value;
    }

    public Result<FlightTaskTool> AddTaskTool(Guid taskId, ToolSnapshot? item,
        ResourceUsage? usage, string? notes, Guid userId, DateTimeOffset now, Guid? returnToRampId = null)
    {
        var found = FindTaskForWrite(taskId, userId, returnToRampId);
        if (found.IsFailure)
            return found.Error;
        var added = found.Value.AddTool(item, usage, notes);
        if (added.IsFailure)
            return added.Error;

        MarkTaskUpdated(found.Value, userId, now);
        return added.Value;
    }

    public Result<FlightTaskTool> UpdateTaskTool(Guid taskId, Guid recordId, ToolSnapshot? item,
        ResourceUsage? usage, string? notes, Guid userId, DateTimeOffset now, Guid? returnToRampId = null)
    {
        var found = FindTaskForWrite(taskId, userId, returnToRampId);
        if (found.IsFailure)
            return found.Error;
        var changed = found.Value.UpdateTool(recordId, item, usage, notes);
        if (changed.IsFailure)
            return changed.Error;
        if (changed.Value)
            MarkTaskUpdated(found.Value, userId, now);
        return found.Value.Tools.Single(record => record.Id == recordId);
    }

    public Result RemoveTaskTool(Guid taskId, Guid recordId, Guid userId, DateTimeOffset now, Guid? returnToRampId = null)
    {
        var found = FindTaskForWrite(taskId, userId, returnToRampId);
        if (found.IsFailure)
            return found.Error;
        var removed = found.Value.RemoveTool(recordId);
        if (removed.IsFailure)
            return removed;

        MarkTaskUpdated(found.Value, userId, now);
        return Result.Success();
    }

    public Result<FlightTaskMaterial> AddTaskMaterial(Guid taskId, MaterialSnapshot? item,
        ResourceUsage? usage, string? notes, Guid userId, DateTimeOffset now, Guid? returnToRampId = null)
    {
        var found = FindTaskForWrite(taskId, userId, returnToRampId);
        if (found.IsFailure)
            return found.Error;
        var added = found.Value.AddMaterial(item, usage, notes);
        if (added.IsFailure)
            return added.Error;

        MarkTaskUpdated(found.Value, userId, now);
        return added.Value;
    }

    public Result<FlightTaskMaterial> UpdateTaskMaterial(Guid taskId, Guid recordId, MaterialSnapshot? item,
        ResourceUsage? usage, string? notes, Guid userId, DateTimeOffset now, Guid? returnToRampId = null)
    {
        var found = FindTaskForWrite(taskId, userId, returnToRampId);
        if (found.IsFailure)
            return found.Error;
        var changed = found.Value.UpdateMaterial(recordId, item, usage, notes);
        if (changed.IsFailure)
            return changed.Error;
        if (changed.Value)
            MarkTaskUpdated(found.Value, userId, now);
        return found.Value.Materials.Single(record => record.Id == recordId);
    }

    public Result RemoveTaskMaterial(Guid taskId, Guid recordId, Guid userId, DateTimeOffset now, Guid? returnToRampId = null)
    {
        var found = FindTaskForWrite(taskId, userId, returnToRampId);
        if (found.IsFailure)
            return found.Error;
        var removed = found.Value.RemoveMaterial(recordId);
        if (removed.IsFailure)
            return removed;

        MarkTaskUpdated(found.Value, userId, now);
        return Result.Success();
    }

    public Result<FlightTaskGeneralSupport> AddTaskGeneralSupport(Guid taskId, GeneralSupportSnapshot? item,
        ResourceUsage? usage, string? notes, Guid userId, DateTimeOffset now, Guid? returnToRampId = null)
    {
        var found = FindTaskForWrite(taskId, userId, returnToRampId);
        if (found.IsFailure)
            return found.Error;
        var added = found.Value.AddGeneralSupport(item, usage, notes);
        if (added.IsFailure)
            return added.Error;

        MarkTaskUpdated(found.Value, userId, now);
        return added.Value;
    }

    public Result<FlightTaskGeneralSupport> UpdateTaskGeneralSupport(Guid taskId, Guid recordId, GeneralSupportSnapshot? item,
        ResourceUsage? usage, string? notes, Guid userId, DateTimeOffset now, Guid? returnToRampId = null)
    {
        var found = FindTaskForWrite(taskId, userId, returnToRampId);
        if (found.IsFailure)
            return found.Error;
        var changed = found.Value.UpdateGeneralSupport(recordId, item, usage, notes);
        if (changed.IsFailure)
            return changed.Error;
        if (changed.Value)
            MarkTaskUpdated(found.Value, userId, now);
        return found.Value.GeneralSupports.Single(record => record.Id == recordId);
    }

    public Result RemoveTaskGeneralSupport(Guid taskId, Guid recordId, Guid userId, DateTimeOffset now, Guid? returnToRampId = null)
    {
        var found = FindTaskForWrite(taskId, userId, returnToRampId);
        if (found.IsFailure)
            return found.Error;
        var removed = found.Value.RemoveGeneralSupport(recordId);
        if (removed.IsFailure)
            return removed;

        MarkTaskUpdated(found.Value, userId, now);
        return Result.Success();
    }

    public Result<FlightTaskPerformer> AddTaskPerformer(Guid taskId, StaffMemberSnapshot? item,
        TimeWindow? window, string? notes, Guid userId, DateTimeOffset now, Guid? returnToRampId = null)
    {
        var found = FindTaskForWrite(taskId, userId, returnToRampId);
        if (found.IsFailure)
            return found.Error;
        var periodCheck = ValidateReturnToRampEmployee(returnToRampId, window);
        if (periodCheck.IsFailure)
            return periodCheck.Error;
        var added = found.Value.AddPerformer(item, window, notes);
        if (added.IsFailure)
            return added.Error;

        MarkTaskUpdated(found.Value, userId, now);
        return added.Value;
    }

    public Result<FlightTaskPerformer> UpdateTaskPerformer(Guid taskId, Guid recordId, StaffMemberSnapshot? item,
        TimeWindow? window, string? notes, Guid userId, DateTimeOffset now, Guid? returnToRampId = null)
    {
        var found = FindTaskForWrite(taskId, userId, returnToRampId);
        if (found.IsFailure)
            return found.Error;
        var periodCheck = ValidateReturnToRampEmployee(returnToRampId, window);
        if (periodCheck.IsFailure)
            return periodCheck.Error;
        var changed = found.Value.UpdatePerformer(recordId, item, window, notes);
        if (changed.IsFailure)
            return changed.Error;
        if (changed.Value)
            MarkTaskUpdated(found.Value, userId, now);
        return found.Value.Performers.Single(record => record.Id == recordId);
    }

    public Result RemoveTaskPerformer(Guid taskId, Guid recordId, Guid userId, DateTimeOffset now, Guid? returnToRampId = null)
    {
        var found = FindTaskForWrite(taskId, userId, returnToRampId);
        if (found.IsFailure)
            return found.Error;
        if (found.Value.Performers.Any(item => item.Id == recordId))
        {
            var periodCheck = ValidateReturnToRampPerformerRemoval(returnToRampId, found.Value.Performers.Count, "Task");
            if (periodCheck.IsFailure)
                return periodCheck;
        }
        var removed = found.Value.RemovePerformer(recordId);
        if (removed.IsFailure)
            return removed;

        MarkTaskUpdated(found.Value, userId, now);
        return Result.Success();
    }

    public Result<FlightTaskAttachment> AddTaskAttachment(Guid taskId, AttachmentDetails? details,
        Guid userId, DateTimeOffset now, Guid? returnToRampId = null)
    {
        var found = FindTaskForWrite(taskId, userId, returnToRampId);
        if (found.IsFailure)
            return found.Error;
        var added = found.Value.AddAttachment(details, userId, now.ToUniversalTime());
        if (added.IsFailure)
            return added.Error;

        MarkTaskUpdated(found.Value, userId, now);
        return added.Value;
    }

    public Result<FlightTaskAttachment> RemoveTaskAttachment(Guid taskId, Guid attachmentId,
        Guid userId, DateTimeOffset now, Guid? returnToRampId = null)
    {
        var found = FindTaskForWrite(taskId, userId, returnToRampId);
        if (found.IsFailure)
            return found.Error;
        var removed = found.Value.RemoveAttachment(attachmentId);
        if (removed.IsFailure)
            return removed.Error;

        MarkTaskUpdated(found.Value, userId, now);
        return removed.Value;
    }

    private Result ValidateTaskWrite(Guid userId, Guid? returnToRampId)
    {
        if (userId == Guid.Empty)
            return Error.Validation("Recording user is required.", "Operations.FlightTask.UserRequired");
        if (_cancellationSubmissions.Count > 0)
            return Error.Conflict("A flight with cancellation submissions cannot have tasks.", "Operations.Flight.CancellationSubmitted");
        return ValidateReturnToRampScope(returnToRampId);
    }

    private Result<FlightTask> FindTaskForWrite(Guid taskId, Guid userId, Guid? returnToRampId)
    {
        var check = ValidateTaskWrite(userId, returnToRampId);
        if (check.IsFailure)
            return check.Error;
        var task = _tasks.SingleOrDefault(item => item.Id == taskId && item.ReturnToRampId == returnToRampId);
        return task is null
            ? Error.NotFound("Task was not found on this flight.", "Operations.FlightTask.NotFound")
            : task;
    }

    private void MarkTaskUpdated(FlightTask task, Guid userId, DateTimeOffset now)
    {
        task.MarkUpdated(userId, now.ToUniversalTime());
        MarkReturnToRampUpdated(task.ReturnToRampId, userId, now);
        UpdatedAtUtc = now.ToUniversalTime();
    }

    /// <summary>Starts an RTR after a recorded flight departure. Load all occurrences and operational records before mutation.</summary>
    public Result<FlightReturnToRamp> StartReturnToRamp(RtrArrivalDetails? arrivalDetails, Guid userId, DateTimeOffset now)
    {
        var check = ValidateReturnToRampWrite(userId);
        if (check.IsFailure)
            return check.Error;
        if (arrivalDetails is null)
            return Error.Validation("RTR arrival details are required.", "Operations.ReturnToRamp.ArrivalDetailsRequired");
        if (_departureSubmissions.Count == 0)
            return Error.Conflict("A flight departure must be recorded before adding RTR.", "Operations.ReturnToRamp.DepartureRequired");
        if (_returnToRamps.Any(item => item.IsOpen))
            return Error.Conflict("Close the open RTR before adding another occurrence.", "Operations.ReturnToRamp.OpenExists");
        if (LastReturnToRampSequence == int.MaxValue)
            return Error.Conflict("The RTR sequence limit has been reached.", "Operations.ReturnToRamp.SequenceExhausted");

        var record = new FlightReturnToRamp(Guid.NewGuid(), Id, LastReturnToRampSequence + 1, arrivalDetails, userId,
            now.ToUniversalTime());
        _returnToRamps.Add(record);
        LastReturnToRampSequence = record.Sequence;
        UpdatedAtUtc = now.ToUniversalTime();
        return record;
    }

    public Result<FlightReturnToRamp> UpdateReturnToRampArrival(Guid returnToRampId, RtrArrivalDetails? arrivalDetails,
        Guid userId, DateTimeOffset now)
    {
        var found = FindReturnToRampForWrite(returnToRampId, userId);
        if (found.IsFailure)
            return found.Error;
        if (arrivalDetails is null)
            return Error.Validation("RTR arrival details are required.", "Operations.ReturnToRamp.ArrivalDetailsRequired");
        var changed = found.Value.UpdateArrival(arrivalDetails);
        if (changed.IsFailure)
            return changed.Error;
        if (changed.Value)
        {
            found.Value.MarkUpdated(userId, now.ToUniversalTime());
            UpdatedAtUtc = now.ToUniversalTime();
        }
        return found.Value;
    }

    /// <summary>Records or corrects RTR departure, validating the complete retained work before changing any details.</summary>
    public Result<FlightReturnToRamp> CloseReturnToRamp(Guid returnToRampId, RtrDepartureDetails? departureDetails,
        Guid userId, DateTimeOffset now)
    {
        var found = FindReturnToRampForWrite(returnToRampId, userId);
        if (found.IsFailure)
            return found.Error;
        if (departureDetails is null)
            return Error.Validation("RTR departure details are required.", "Operations.ReturnToRamp.DepartureDetailsRequired");
        var changed = found.Value.SetDeparture(departureDetails, userId);
        if (changed.IsFailure)
            return changed.Error;
        if (changed.Value)
        {
            found.Value.MarkUpdated(userId, now.ToUniversalTime());
            UpdatedAtUtc = now.ToUniversalTime();
        }
        return found.Value;
    }

    public Result<FlightReturnToRamp> RemoveReturnToRamp(Guid returnToRampId, Guid userId, DateTimeOffset now)
    {
        var found = FindReturnToRampForWrite(returnToRampId, userId);
        if (found.IsFailure)
            return found.Error;
        _services.RemoveAll(item => item.ReturnToRampId == returnToRampId);
        _tasks.RemoveAll(item => item.ReturnToRampId == returnToRampId);
        _returnToRamps.Remove(found.Value);
        UpdatedAtUtc = now.ToUniversalTime();
        return found.Value;
    }

    public Result<FlightService> RemoveService(Guid serviceId, Guid userId, DateTimeOffset now, Guid? returnToRampId = null)
    {
        var found = FindServiceForWrite(serviceId, userId, returnToRampId);
        if (found.IsFailure)
            return found.Error;
        if (found.Value.HasOtherUserEnd)
            return Error.Forbidden("A service with another employee's end time cannot be deleted.", "Operations.FlightService.DeleteReadOnly");
        if (returnToRampId is { } occurrenceId)
        {
            var occurrence = _returnToRamps.Single(item => item.Id == occurrenceId);
            var check = occurrence.ValidateActivityRemoval();
            if (check.IsFailure)
                return check.Error;
            occurrence.RemoveService(found.Value);
            occurrence.MarkUpdated(userId, now.ToUniversalTime());
        }
        _services.Remove(found.Value);
        UpdatedAtUtc = now.ToUniversalTime();
        return found.Value;
    }

    public Result<FlightTask> RemoveTask(Guid taskId, Guid userId, DateTimeOffset now, Guid? returnToRampId = null)
    {
        var found = FindTaskForWrite(taskId, userId, returnToRampId);
        if (found.IsFailure)
            return found.Error;
        if (returnToRampId is { } occurrenceId)
        {
            var occurrence = _returnToRamps.Single(item => item.Id == occurrenceId);
            var check = occurrence.ValidateActivityRemoval();
            if (check.IsFailure)
                return check.Error;
            occurrence.RemoveTask(found.Value);
            occurrence.MarkUpdated(userId, now.ToUniversalTime());
        }
        _tasks.Remove(found.Value);
        UpdatedAtUtc = now.ToUniversalTime();
        return found.Value;
    }

    private Result ValidateReturnToRampWrite(Guid userId)
    {
        if (userId == Guid.Empty)
            return Error.Validation("Recording user is required.", "Operations.ReturnToRamp.UserRequired");
        if (_cancellationSubmissions.Count > 0)
            return Error.Conflict("A flight with cancellation submissions cannot have RTR records.", "Operations.Flight.CancellationSubmitted");
        return Result.Success();
    }

    private Result<FlightReturnToRamp> FindReturnToRampForWrite(Guid returnToRampId, Guid userId)
    {
        var check = ValidateReturnToRampWrite(userId);
        if (check.IsFailure)
            return check.Error;
        var record = _returnToRamps.SingleOrDefault(item => item.Id == returnToRampId);
        return record is null
            ? Error.NotFound("RTR record was not found on this flight.", "Operations.ReturnToRamp.NotFound")
            : record;
    }

    private Result ValidateReturnToRampScope(Guid? returnToRampId) =>
        returnToRampId.HasValue && _returnToRamps.All(item => item.Id != returnToRampId.Value)
            ? Error.NotFound("RTR record was not found on this flight.", "Operations.ReturnToRamp.NotFound")
            : Result.Success();

    private Result ValidateReturnToRampService(Guid? returnToRampId, TimeWindow? window, IEnumerable<TimeWindow?> employees) =>
        returnToRampId is { } id
            ? _returnToRamps.Single(item => item.Id == id).ValidateService(window, employees)
            : Result.Success();

    private Result ValidateReturnToRampTask(Guid? returnToRampId, TimeWindow? window, IEnumerable<TimeWindow?> employees) =>
        returnToRampId is { } id
            ? _returnToRamps.Single(item => item.Id == id).ValidateTask(window, employees)
            : Result.Success();

    private Result ValidateReturnToRampEmployee(Guid? returnToRampId, TimeWindow? window) =>
        returnToRampId is { } id
            ? _returnToRamps.Single(item => item.Id == id).ValidateEmployeeWindow(window)
            : Result.Success();

    private Result ValidateReturnToRampPerformerRemoval(Guid? returnToRampId, int count, string kind) =>
        returnToRampId is { } id
            ? _returnToRamps.Single(item => item.Id == id).ValidatePerformerRemoval(count, kind)
            : Result.Success();

    private void MarkReturnToRampUpdated(Guid? returnToRampId, Guid userId, DateTimeOffset now)
    {
        if (returnToRampId is { } id)
            _returnToRamps.Single(item => item.Id == id).MarkUpdated(userId, now.ToUniversalTime());
    }

    private void SetPlannedServices(IReadOnlyList<ServiceSnapshot> services)
    {
        _plannedServices.Clear();
        foreach (var service in services.DistinctBy(service => service.ServiceId))
            _plannedServices.Add(new PlannedService(Guid.NewGuid(), Id, service));
    }

    private void SetAssignedEmployees(IReadOnlyList<StaffMemberSnapshot> employees)
    {
        _assignedEmployees.Clear();
        foreach (var employee in employees.DistinctBy(employee => employee.StaffMemberId))
            _assignedEmployees.Add(new FlightAssignedEmployee(Guid.NewGuid(), Id, employee));
    }

    private static Result ValidateCustomerFlightNumber(CustomerSnapshot customer, FlightNumber number)
    {
        var validated = FlightNumber.Create(number.Value, customer.IataCode);
        if (validated.IsFailure)
            return validated.Error;

        var prefix = customer.IataCode?.Trim().ToUpperInvariant();
        if (number != validated.Value)
            return Error.Validation(
                $"Flight number must start with customer IATA code '{prefix}' followed by the flight number.",
                "Operations.FlightNumber.CustomerPrefixRequired");

        return Result.Success();
    }

    private static Error PerLandingAssignmentError() => Error.Validation(
        "Per Landing flights are station-wide and cannot have assigned staff.",
        "Operations.PerLanding.AssignmentNotAllowed");
}
