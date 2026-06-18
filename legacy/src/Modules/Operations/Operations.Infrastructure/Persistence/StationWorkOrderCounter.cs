namespace Operations.Infrastructure.Persistence;

/// <summary>Per-station monotonic counter for work order numbers (infrastructure-only).</summary>
public sealed class StationWorkOrderCounter
{
    public Guid StationId { get; set; }
    public long LastSequence { get; set; }
}
