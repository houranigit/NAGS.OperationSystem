using BuildingBlocks.Domain.Entities;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

public sealed class FlightTaskTool : Entity<Guid>
{
    private FlightTaskTool() { }

    internal FlightTaskTool(Guid id, Guid flightTaskId, ToolSnapshot item, ResourceUsage usage, string? notes)
    {
        Id = id;
        FlightTaskId = flightTaskId;
        Update(item, usage, notes);
    }

    public Guid FlightTaskId { get; private set; }
    public ToolSnapshot Tool { get; private set; } = null!;
    public ResourceUsage Usage { get; private set; } = null!;
    public string? Notes { get; private set; }

    internal void Update(ToolSnapshot item, ResourceUsage usage, string? notes)
    {
        Tool = new ToolSnapshot(item.ToolId, item.Name, item.CalculationType);
        Usage = usage.Copy();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }
}
