using Core.Contracts.Features.AircraftType;

namespace Core.Contracts.Readers;

public interface IAircraftTypeReader
{
    Task<AircraftTypeSnapshot?> GetByIdAsync(Guid aircraftTypeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// All active aircraft types. Used by the mobile work-order forms to present the
    /// aircraft-type picker in a single network round-trip.
    /// </summary>
    Task<IReadOnlyList<AircraftTypeSnapshot>> ListActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>True when an aircraft type with this id exists AND is active.</summary>
    Task<bool> ExistsActiveAsync(Guid aircraftTypeId, CancellationToken cancellationToken = default);
}
