namespace Operations.Domain.StationWorkOrderSequence;

/// <summary>Monotonic per-station sequence for formatting <see cref="ValueObjects.WorkOrderNumber"/>.</summary>
public interface IStationWorkOrderSequenceRepository
{
    /// <summary>Returns the next 1-based sequence for the given station, persisted atomically.</summary>
    Task<long> GetNextAsync(Guid stationId, CancellationToken cancellationToken = default);
}
