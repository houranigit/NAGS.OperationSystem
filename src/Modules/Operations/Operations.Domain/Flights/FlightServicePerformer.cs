using BuildingBlocks.Domain.Entities;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

/// <summary>An employee's individual work period on a performed service.</summary>
public sealed class FlightServicePerformer : Entity<Guid>
{
    private FlightServicePerformer() { }

    internal FlightServicePerformer(Guid id, Guid flightServiceId, StaffMemberSnapshot employee, TimeWindow window)
    {
        Id = id;
        FlightServiceId = flightServiceId;
        Update(employee, window);
    }

    public Guid FlightServiceId { get; private set; }
    public StaffMemberSnapshot Employee { get; private set; } = null!;
    public TimeWindow Window { get; private set; } = null!;

    internal void Update(StaffMemberSnapshot employee, TimeWindow window)
    {
        Employee = new StaffMemberSnapshot(employee.StaffMemberId, employee.FullName, employee.EmployeeId);
        Window = window.Copy();
    }
}
