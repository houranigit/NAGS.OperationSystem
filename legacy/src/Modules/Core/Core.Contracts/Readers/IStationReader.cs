using Core.Contracts.Features.Station;

namespace Core.Contracts.Readers;

public interface IStationReader
{
    Task<StationSnapshot?> GetByIdAsync(Guid stationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StationSnapshot>> GetManyAsync(
        IReadOnlyList<Guid> stationIds,
        CancellationToken cancellationToken = default);

    /// <summary>True when a station with this id exists AND is active.</summary>
    Task<bool> ExistsActiveAsync(Guid stationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subset of <paramref name="stationIds"/> that exist but are inactive (or do
    /// not exist at all). Empty result means every id maps to an active station.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetInactiveOrMissingIdsAsync(
        IReadOnlyList<Guid> stationIds,
        CancellationToken cancellationToken = default);
}
