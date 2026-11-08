using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Auditing;
using BuildingBlocks.Domain.Results;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

/// <summary>A flight with planning data and mutually exclusive cancellation or operational details.</summary>
public sealed class Flight : AggregateRoot<Guid>, IAuditable
{
    private readonly List<PlannedService> _plannedServices = [];
    private readonly List<FlightAssignedEmployee> _assignedEmployees = [];
    private readonly List<FlightArrivalSubmission> _arrivalSubmissions = [];
    private readonly List<FlightDepartureSubmission> _departureSubmissions = [];
    private readonly List<FlightCancellationSubmission> _cancellationSubmissions = [];
    private readonly List<FlightService> _services = [];
    private readonly List<FlightTask> _tasks = [];

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
    public IReadOnlyList<FlightService> Services => _services.AsReadOnly();
    public IReadOnlyList<FlightTask> Tasks => _tasks.AsReadOnly();
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

    /// <summary>Records or updates one employee's cancellation. Existing tasks, services, and submission collections must be loaded.</summary>
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
        if (_arrivalSubmissions.Count > 0 || _departureSubmissions.Count > 0 || _services.Count > 0 || _tasks.Count > 0)
            return Error.Conflict("A flight with operational records cannot have cancellation submissions.", "Operations.CancellationSubmission.OperationsAlreadyRecorded");

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
        if (_cancellationSubmissions.Count > 0)
            return Error.Conflict("A flight with cancellation submissions cannot have arrival submissions.", "Operations.Flight.CancellationSubmitted");

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
        if (_cancellationSubmissions.Count > 0)
            return Error.Conflict("A flight with cancellation submissions cannot have departure submissions.", "Operations.Flight.CancellationSubmitted");

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
        TimeWindow? window, string? notes, Guid userId, DateTimeOffset now)
    {
        var check = ValidateServiceWrite(userId);
        if (check.IsFailure)
            return check.Error;
        var created = FlightService.Create(Id, service, customerReferenceNumber, window, notes, userId, now.ToUniversalTime());
        if (created.IsFailure)
            return created.Error;

        _services.Add(created.Value);
        UpdatedAtUtc = now.ToUniversalTime();
        return created.Value;
    }

    public Result<FlightService> UpdateService(Guid serviceId, ServiceSnapshot? service, string? customerReferenceNumber,
        TimeWindow? window, string? notes, Guid userId, DateTimeOffset now)
    {
        var found = FindServiceForWrite(serviceId, userId);
        if (found.IsFailure)
            return found.Error;
        var changed = found.Value.UpdateDetails(service, customerReferenceNumber, window, notes, userId);
        if (changed.IsFailure)
            return changed.Error;
        if (changed.Value)
            MarkServiceUpdated(found.Value, userId, now);
        return found.Value;
    }

    public Result<FlightServicePerformer> AddServicePerformer(Guid serviceId, StaffMemberSnapshot? employee,
        TimeWindow? window, Guid userId, DateTimeOffset now)
    {
        var found = FindServiceForWrite(serviceId, userId);
        if (found.IsFailure)
            return found.Error;
        var added = found.Value.AddPerformer(employee, window);
        if (added.IsFailure)
            return added.Error;

        MarkServiceUpdated(found.Value, userId, now);
        return added.Value;
    }

    public Result<FlightServicePerformer> UpdateServicePerformer(Guid serviceId, Guid performerId,
        StaffMemberSnapshot? employee, TimeWindow? window, Guid userId, DateTimeOffset now)
    {
        var found = FindServiceForWrite(serviceId, userId);
        if (found.IsFailure)
            return found.Error;
        var changed = found.Value.UpdatePerformer(performerId, employee, window);
        if (changed.IsFailure)
            return changed.Error;
        if (changed.Value)
            MarkServiceUpdated(found.Value, userId, now);
        return found.Value.Performers.Single(performer => performer.Id == performerId);
    }

    public Result RemoveServicePerformer(Guid serviceId, Guid performerId, Guid userId, DateTimeOffset now)
    {
        var found = FindServiceForWrite(serviceId, userId);
        if (found.IsFailure)
            return found.Error;
        var removed = found.Value.RemovePerformer(performerId);
        if (removed.IsFailure)
            return removed;

        MarkServiceUpdated(found.Value, userId, now);
        return Result.Success();
    }

    public Result<FlightServiceAttachment> AddServiceAttachment(Guid serviceId, AttachmentDetails? details,
        Guid userId, DateTimeOffset now)
    {
        var found = FindServiceForWrite(serviceId, userId);
        if (found.IsFailure)
            return found.Error;
        var added = found.Value.AddAttachment(details, userId, now.ToUniversalTime());
        if (added.IsFailure)
            return added.Error;

        MarkServiceUpdated(found.Value, userId, now);
        return added.Value;
    }

    public Result<FlightServiceAttachment> RemoveServiceAttachment(Guid serviceId, Guid attachmentId,
        Guid userId, DateTimeOffset now)
    {
        var found = FindServiceForWrite(serviceId, userId);
        if (found.IsFailure)
            return found.Error;
        var removed = found.Value.RemoveAttachment(attachmentId);
        if (removed.IsFailure)
            return removed.Error;

        MarkServiceUpdated(found.Value, userId, now);
        return removed.Value;
    }

    private Result ValidateServiceWrite(Guid userId)
    {
        if (userId == Guid.Empty)
            return Error.Validation("Recording user is required.", "Operations.FlightService.UserRequired");
        if (_cancellationSubmissions.Count > 0)
            return Error.Conflict("A flight with cancellation submissions cannot have performed services.", "Operations.Flight.CancellationSubmitted");
        return Result.Success();
    }

    private Result<FlightService> FindServiceForWrite(Guid serviceId, Guid userId)
    {
        var check = ValidateServiceWrite(userId);
        if (check.IsFailure)
            return check.Error;
        var service = _services.SingleOrDefault(item => item.Id == serviceId);
        return service is null
            ? Error.NotFound("Performed service was not found on this flight.", "Operations.FlightService.NotFound")
            : service;
    }

    private void MarkServiceUpdated(FlightService service, Guid userId, DateTimeOffset now)
    {
        service.MarkUpdated(userId, now.ToUniversalTime());
        UpdatedAtUtc = now.ToUniversalTime();
    }

    /// <summary>Records a task. Existing tasks and cancellation submissions must be loaded.</summary>
    public Result<FlightTask> AddTask(FlightTaskType taskType, AtaChapterSnapshot? ataChapter,
        string? customerReferenceNumber, TimeWindow? window, string? notes, Guid userId, DateTimeOffset now)
    {
        var check = ValidateTaskWrite(userId);
        if (check.IsFailure)
            return check.Error;
        var created = FlightTask.Create(Id, taskType, ataChapter, customerReferenceNumber, window, notes, userId, now.ToUniversalTime());
        if (created.IsFailure)
            return created.Error;

        _tasks.Add(created.Value);
        UpdatedAtUtc = now.ToUniversalTime();
        return created.Value;
    }

    public Result<FlightTask> UpdateTask(Guid taskId, FlightTaskType taskType, AtaChapterSnapshot? ataChapter,
        string? customerReferenceNumber, TimeWindow? window, string? notes, Guid userId, DateTimeOffset now)
    {
        var found = FindTaskForWrite(taskId, userId);
        if (found.IsFailure)
            return found.Error;
        var changed = found.Value.UpdateDetails(taskType, ataChapter, customerReferenceNumber, window, notes, userId);
        if (changed.IsFailure)
            return changed.Error;
        if (changed.Value)
            MarkTaskUpdated(found.Value, userId, now);
        return found.Value;
    }

    public Result<FlightTaskTool> AddTaskTool(Guid taskId, ToolSnapshot? item,
        ResourceUsage? usage, string? notes, Guid userId, DateTimeOffset now)
    {
        var found = FindTaskForWrite(taskId, userId);
        if (found.IsFailure)
            return found.Error;
        var added = found.Value.AddTool(item, usage, notes);
        if (added.IsFailure)
            return added.Error;

        MarkTaskUpdated(found.Value, userId, now);
        return added.Value;
    }

    public Result<FlightTaskTool> UpdateTaskTool(Guid taskId, Guid recordId, ToolSnapshot? item,
        ResourceUsage? usage, string? notes, Guid userId, DateTimeOffset now)
    {
        var found = FindTaskForWrite(taskId, userId);
        if (found.IsFailure)
            return found.Error;
        var changed = found.Value.UpdateTool(recordId, item, usage, notes);
        if (changed.IsFailure)
            return changed.Error;
        if (changed.Value)
            MarkTaskUpdated(found.Value, userId, now);
        return found.Value.Tools.Single(record => record.Id == recordId);
    }

    public Result RemoveTaskTool(Guid taskId, Guid recordId, Guid userId, DateTimeOffset now)
    {
        var found = FindTaskForWrite(taskId, userId);
        if (found.IsFailure)
            return found.Error;
        var removed = found.Value.RemoveTool(recordId);
        if (removed.IsFailure)
            return removed;

        MarkTaskUpdated(found.Value, userId, now);
        return Result.Success();
    }

    public Result<FlightTaskMaterial> AddTaskMaterial(Guid taskId, MaterialSnapshot? item,
        ResourceUsage? usage, string? notes, Guid userId, DateTimeOffset now)
    {
        var found = FindTaskForWrite(taskId, userId);
        if (found.IsFailure)
            return found.Error;
        var added = found.Value.AddMaterial(item, usage, notes);
        if (added.IsFailure)
            return added.Error;

        MarkTaskUpdated(found.Value, userId, now);
        return added.Value;
    }

    public Result<FlightTaskMaterial> UpdateTaskMaterial(Guid taskId, Guid recordId, MaterialSnapshot? item,
        ResourceUsage? usage, string? notes, Guid userId, DateTimeOffset now)
    {
        var found = FindTaskForWrite(taskId, userId);
        if (found.IsFailure)
            return found.Error;
        var changed = found.Value.UpdateMaterial(recordId, item, usage, notes);
        if (changed.IsFailure)
            return changed.Error;
        if (changed.Value)
            MarkTaskUpdated(found.Value, userId, now);
        return found.Value.Materials.Single(record => record.Id == recordId);
    }

    public Result RemoveTaskMaterial(Guid taskId, Guid recordId, Guid userId, DateTimeOffset now)
    {
        var found = FindTaskForWrite(taskId, userId);
        if (found.IsFailure)
            return found.Error;
        var removed = found.Value.RemoveMaterial(recordId);
        if (removed.IsFailure)
            return removed;

        MarkTaskUpdated(found.Value, userId, now);
        return Result.Success();
    }

    public Result<FlightTaskGeneralSupport> AddTaskGeneralSupport(Guid taskId, GeneralSupportSnapshot? item,
        ResourceUsage? usage, string? notes, Guid userId, DateTimeOffset now)
    {
        var found = FindTaskForWrite(taskId, userId);
        if (found.IsFailure)
            return found.Error;
        var added = found.Value.AddGeneralSupport(item, usage, notes);
        if (added.IsFailure)
            return added.Error;

        MarkTaskUpdated(found.Value, userId, now);
        return added.Value;
    }

    public Result<FlightTaskGeneralSupport> UpdateTaskGeneralSupport(Guid taskId, Guid recordId, GeneralSupportSnapshot? item,
        ResourceUsage? usage, string? notes, Guid userId, DateTimeOffset now)
    {
        var found = FindTaskForWrite(taskId, userId);
        if (found.IsFailure)
            return found.Error;
        var changed = found.Value.UpdateGeneralSupport(recordId, item, usage, notes);
        if (changed.IsFailure)
            return changed.Error;
        if (changed.Value)
            MarkTaskUpdated(found.Value, userId, now);
        return found.Value.GeneralSupports.Single(record => record.Id == recordId);
    }

    public Result RemoveTaskGeneralSupport(Guid taskId, Guid recordId, Guid userId, DateTimeOffset now)
    {
        var found = FindTaskForWrite(taskId, userId);
        if (found.IsFailure)
            return found.Error;
        var removed = found.Value.RemoveGeneralSupport(recordId);
        if (removed.IsFailure)
            return removed;

        MarkTaskUpdated(found.Value, userId, now);
        return Result.Success();
    }

    public Result<FlightTaskPerformer> AddTaskPerformer(Guid taskId, StaffMemberSnapshot? item,
        TimeWindow? window, string? notes, Guid userId, DateTimeOffset now)
    {
        var found = FindTaskForWrite(taskId, userId);
        if (found.IsFailure)
            return found.Error;
        var added = found.Value.AddPerformer(item, window, notes);
        if (added.IsFailure)
            return added.Error;

        MarkTaskUpdated(found.Value, userId, now);
        return added.Value;
    }

    public Result<FlightTaskPerformer> UpdateTaskPerformer(Guid taskId, Guid recordId, StaffMemberSnapshot? item,
        TimeWindow? window, string? notes, Guid userId, DateTimeOffset now)
    {
        var found = FindTaskForWrite(taskId, userId);
        if (found.IsFailure)
            return found.Error;
        var changed = found.Value.UpdatePerformer(recordId, item, window, notes);
        if (changed.IsFailure)
            return changed.Error;
        if (changed.Value)
            MarkTaskUpdated(found.Value, userId, now);
        return found.Value.Performers.Single(record => record.Id == recordId);
    }

    public Result RemoveTaskPerformer(Guid taskId, Guid recordId, Guid userId, DateTimeOffset now)
    {
        var found = FindTaskForWrite(taskId, userId);
        if (found.IsFailure)
            return found.Error;
        var removed = found.Value.RemovePerformer(recordId);
        if (removed.IsFailure)
            return removed;

        MarkTaskUpdated(found.Value, userId, now);
        return Result.Success();
    }

    public Result<FlightTaskAttachment> AddTaskAttachment(Guid taskId, AttachmentDetails? details,
        Guid userId, DateTimeOffset now)
    {
        var found = FindTaskForWrite(taskId, userId);
        if (found.IsFailure)
            return found.Error;
        var added = found.Value.AddAttachment(details, userId, now.ToUniversalTime());
        if (added.IsFailure)
            return added.Error;

        MarkTaskUpdated(found.Value, userId, now);
        return added.Value;
    }

    public Result<FlightTaskAttachment> RemoveTaskAttachment(Guid taskId, Guid attachmentId,
        Guid userId, DateTimeOffset now)
    {
        var found = FindTaskForWrite(taskId, userId);
        if (found.IsFailure)
            return found.Error;
        var removed = found.Value.RemoveAttachment(attachmentId);
        if (removed.IsFailure)
            return removed.Error;

        MarkTaskUpdated(found.Value, userId, now);
        return removed.Value;
    }

    private Result ValidateTaskWrite(Guid userId)
    {
        if (userId == Guid.Empty)
            return Error.Validation("Recording user is required.", "Operations.FlightTask.UserRequired");
        if (_cancellationSubmissions.Count > 0)
            return Error.Conflict("A flight with cancellation submissions cannot have tasks.", "Operations.Flight.CancellationSubmitted");
        return Result.Success();
    }

    private Result<FlightTask> FindTaskForWrite(Guid taskId, Guid userId)
    {
        var check = ValidateTaskWrite(userId);
        if (check.IsFailure)
            return check.Error;
        var task = _tasks.SingleOrDefault(item => item.Id == taskId);
        return task is null
            ? Error.NotFound("Task was not found on this flight.", "Operations.FlightTask.NotFound")
            : task;
    }

    private void MarkTaskUpdated(FlightTask task, Guid userId, DateTimeOffset now)
    {
        task.MarkUpdated(userId, now.ToUniversalTime());
        UpdatedAtUtc = now.ToUniversalTime();
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
