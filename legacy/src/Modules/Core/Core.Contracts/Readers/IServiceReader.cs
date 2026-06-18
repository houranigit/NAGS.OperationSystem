using Core.Contracts.Features.Service;

namespace Core.Contracts.Readers;

public interface IServiceReader
{
    Task<ServiceSnapshot?> GetByIdAsync(Guid serviceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceSnapshot>> GetManyAsync(
        IReadOnlyList<Guid> serviceIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// All active services. Used by the mobile work-order forms to populate the service
    /// picker in a single network round-trip.
    /// </summary>
    /// <param name="excludeAog">
    /// When <c>true</c>, the AOG seed service is filtered out at the SQL layer so it is
    /// never fetched into memory. Callers serving the mobile work-order pickers pass
    /// <c>true</c> because work orders cannot bill AOG.
    /// </param>
    Task<IReadOnlyList<ServiceSnapshot>> ListActiveAsync(
        bool excludeAog = false,
        CancellationToken cancellationToken = default);

    /// <summary>True when a service with this id exists AND is active.</summary>
    Task<bool> ExistsActiveAsync(Guid serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subset of <paramref name="serviceIds"/> that exist but are inactive (or do
    /// not exist at all). Empty result means every id maps to an active service.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetInactiveOrMissingIdsAsync(
        IReadOnlyList<Guid> serviceIds,
        CancellationToken cancellationToken = default);
}
