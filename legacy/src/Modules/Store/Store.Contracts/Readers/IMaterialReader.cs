using Store.Contracts.Features.Material;

namespace Store.Contracts.Readers;

public interface IMaterialReader
{
    Task<MaterialSnapshot?> GetByIdAsync(Guid materialId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaterialSnapshot>> GetManyAsync(
        IReadOnlyList<Guid> materialIds,
        CancellationToken cancellationToken = default);

    /// <summary>All active materials — used by the mobile bootstrap to populate the work-order task editor offline.</summary>
    Task<IReadOnlyList<MaterialSnapshot>> ListActiveAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsActiveAsync(Guid materialId, CancellationToken cancellationToken = default);
}
