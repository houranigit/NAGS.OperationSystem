using BuildingBlocks.Domain.Entities;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

/// <summary>A staff member assigned to a flight. Controls visibility for non-Per-Landing flights.</summary>
public sealed class FlightAssignedEmployee : Entity<Guid>
{
    private FlightAssignedEmployee() { }

    internal FlightAssignedEmployee(Guid id, Guid flightId, StaffMemberSnapshot employee)
    {
        Id = id;
        FlightId = flightId;
        Employee = employee;
    }

    public Guid FlightId { get; private set; }
    public StaffMemberSnapshot Employee { get; private set; } = null!;
}
