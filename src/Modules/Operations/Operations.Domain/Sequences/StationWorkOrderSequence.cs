using BuildingBlocks.Domain.Entities;

namespace Operations.Domain.Sequences;

/// <summary>
/// Per-station monotonic counter for human-facing work-order numbers. Allocation is the explicit
/// exception to optimistic concurrency and is done under a serializable transaction in infrastructure.
/// </summary>
public sealed class StationWorkOrderSequence : Entity<Guid>
{
    private StationWorkOrderSequence() { }

    public StationWorkOrderSequence(Guid stationId, string stationIata)
    {
        Id = stationId;
        StationIata = stationIata;
        LastValue = 0;
    }

    /// <summary>The station id (also the primary key).</summary>
    public string StationIata { get; private set; } = null!;
    public int LastValue { get; private set; }

    public int Next()
    {
        LastValue += 1;
        return LastValue;
    }
}
