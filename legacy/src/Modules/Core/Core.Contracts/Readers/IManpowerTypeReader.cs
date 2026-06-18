using Core.Contracts.Features.ManpowerType;

namespace Core.Contracts.Readers;

public interface IManpowerTypeReader
{
    Task<ManpowerTypeSnapshot?> GetByIdAsync(Guid manpowerTypeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ManpowerTypeSnapshot>> GetManyAsync(
        IReadOnlyList<Guid> manpowerTypeIds,
        CancellationToken cancellationToken = default);

    /// <summary>True when a manpower type with this id exists AND is active.</summary>
    Task<bool> ExistsActiveAsync(Guid manpowerTypeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subset of <paramref name="manpowerTypeIds"/> that exist but are inactive (or
    /// do not exist at all). Empty result means every id maps to an active manpower type.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetInactiveOrMissingIdsAsync(
        IReadOnlyList<Guid> manpowerTypeIds,
        CancellationToken cancellationToken = default);
}
