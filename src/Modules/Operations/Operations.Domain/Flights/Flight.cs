using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Auditing;
using BuildingBlocks.Domain.Results;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

/// <summary>A scheduled flight with its planned services and assigned staff.</summary>
public sealed class Flight : AggregateRoot<Guid>, IAuditable
{
    private readonly List<PlannedService> _plannedServices = [];
    private readonly List<FlightAssignedEmployee> _assignedEmployees = [];

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
