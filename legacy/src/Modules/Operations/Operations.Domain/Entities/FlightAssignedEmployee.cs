using BuildingBlocks.Domain.Entities;
using Core.Contracts.Features.Employee;
using Operations.Domain.Aggregates.Flight;

namespace Operations.Domain.Entities;

public sealed class FlightAssignedEmployee : Entity<Guid>
{
    public FlightId FlightId { get; private set; } = null!;
    public EmployeeSnapshot Employee { get; private set; } = null!;

    private FlightAssignedEmployee()
    {
    }

    internal FlightAssignedEmployee(Guid id, FlightId flightId, EmployeeSnapshot employee)
    {
        Id = id;
        FlightId = flightId;
        Employee = employee;
    }
}
