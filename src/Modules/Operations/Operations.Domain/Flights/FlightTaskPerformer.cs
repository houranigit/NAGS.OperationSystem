using BuildingBlocks.Domain.Entities;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

public sealed class FlightTaskPerformer : Entity<Guid>
{
    private FlightTaskPerformer() { }

    internal FlightTaskPerformer(Guid id, Guid flightTaskId, StaffMemberSnapshot item, TimeWindow window, string? notes)
    {
        Id = id;
        FlightTaskId = flightTaskId;
        Update(item, window, notes);
    }

    public Guid FlightTaskId { get; private set; }
    public StaffMemberSnapshot Employee { get; private set; } = null!;
    public TimeWindow Window { get; private set; } = null!;
    public string? Notes { get; private set; }

    internal void Update(StaffMemberSnapshot item, TimeWindow window, string? notes)
    {
        Employee = new StaffMemberSnapshot(item.StaffMemberId, item.FullName, item.EmployeeId);
        Window = window.Copy();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }
}
