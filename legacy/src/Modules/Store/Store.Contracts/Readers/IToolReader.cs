using Store.Contracts.Features.Tool;

namespace Store.Contracts.Readers;

public interface IToolReader
{
    Task<ToolSnapshot?> GetByIdAsync(Guid toolId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ToolSnapshot>> GetManyAsync(
        IReadOnlyList<Guid> toolIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// All active tools — used by the mobile bootstrap so the work-order task editor can
    /// pick tools fully offline. Sorted by name for stable display.
    /// </summary>
    Task<IReadOnlyList<ToolSnapshot>> ListActiveAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsActiveAsync(Guid toolId, CancellationToken cancellationToken = default);
}
