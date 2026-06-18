using Store.Contracts.Features.Unit;

namespace Store.Contracts.Readers;

public interface IUnitReader
{
    Task<UnitSnapshot?> GetByIdAsync(Guid unitId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UnitSnapshot>> GetManyAsync(
        IReadOnlyList<Guid> unitIds,
        CancellationToken cancellationToken = default);

    /// <summary>True when a unit with this id exists AND is active.</summary>
    Task<bool> ExistsActiveAsync(Guid unitId, CancellationToken cancellationToken = default);
}
