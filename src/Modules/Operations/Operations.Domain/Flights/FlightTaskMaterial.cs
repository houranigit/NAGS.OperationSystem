using BuildingBlocks.Domain.Entities;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

public sealed class FlightTaskMaterial : Entity<Guid>
{
    private FlightTaskMaterial() { }

    internal FlightTaskMaterial(Guid id, Guid flightTaskId, MaterialSnapshot item, ResourceUsage usage, string? notes)
    {
        Id = id;
        FlightTaskId = flightTaskId;
        Update(item, usage, notes);
    }

    public Guid FlightTaskId { get; private set; }
    public MaterialSnapshot Material { get; private set; } = null!;
    public ResourceUsage Usage { get; private set; } = null!;
    public string? Notes { get; private set; }

    internal void Update(MaterialSnapshot item, ResourceUsage usage, string? notes)
    {
        Material = new MaterialSnapshot(item.MaterialId, item.Name, item.CalculationType);
        Usage = usage.Copy();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }
}
