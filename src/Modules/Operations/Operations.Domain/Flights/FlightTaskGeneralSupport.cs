using BuildingBlocks.Domain.Entities;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

public sealed class FlightTaskGeneralSupport : Entity<Guid>
{
    private FlightTaskGeneralSupport() { }

    internal FlightTaskGeneralSupport(Guid id, Guid flightTaskId, GeneralSupportSnapshot item, ResourceUsage usage, string? notes)
    {
        Id = id;
        FlightTaskId = flightTaskId;
        Update(item, usage, notes);
    }

    public Guid FlightTaskId { get; private set; }
    public GeneralSupportSnapshot GeneralSupport { get; private set; } = null!;
    public ResourceUsage Usage { get; private set; } = null!;
    public string? Notes { get; private set; }

    internal void Update(GeneralSupportSnapshot item, ResourceUsage usage, string? notes)
    {
        GeneralSupport = new GeneralSupportSnapshot(item.GeneralSupportId, item.Name, item.CalculationType);
        Usage = usage.Copy();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }
}
