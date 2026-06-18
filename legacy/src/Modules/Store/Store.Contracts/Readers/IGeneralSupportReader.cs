using Store.Contracts.Features.GeneralSupport;

namespace Store.Contracts.Readers;

public interface IGeneralSupportReader
{
    Task<GeneralSupportSnapshot?> GetByIdAsync(Guid generalSupportId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GeneralSupportSnapshot>> GetManyAsync(
        IReadOnlyList<Guid> generalSupportIds,
        CancellationToken cancellationToken = default);

    /// <summary>All active general supports — used by the mobile bootstrap to populate the work-order task editor offline.</summary>
    Task<IReadOnlyList<GeneralSupportSnapshot>> ListActiveAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsActiveAsync(Guid generalSupportId, CancellationToken cancellationToken = default);
}
