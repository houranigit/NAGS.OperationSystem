using Microsoft.EntityFrameworkCore;
using Store.Contracts.Features.GeneralSupport;
using Store.Contracts.Readers;
using Store.Domain.Aggregates.GeneralSupport;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Readers;

internal sealed class GeneralSupportReader(StoreDbContext context) : IGeneralSupportReader
{
    public async Task<GeneralSupportSnapshot?> GetByIdAsync(Guid generalSupportId, CancellationToken cancellationToken = default)
    {
        var entity = await context.GeneralSupports
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == GeneralSupportId.From(generalSupportId), cancellationToken);

        return entity is null ? null : new GeneralSupportSnapshot(entity.Id.Value, entity.Name);
    }

    public async Task<IReadOnlyList<GeneralSupportSnapshot>> GetManyAsync(
        IReadOnlyList<Guid> generalSupportIds,
        CancellationToken cancellationToken = default)
    {
        if (generalSupportIds.Count == 0) return [];

        var typedIds = generalSupportIds.Select(GeneralSupportId.From).ToList();
        var entities = await context.GeneralSupports
            .AsNoTracking()
            .Where(g => typedIds.Contains(g.Id))
            .ToListAsync(cancellationToken);

        return entities.Select(g => new GeneralSupportSnapshot(g.Id.Value, g.Name)).ToList();
    }

    public async Task<IReadOnlyList<GeneralSupportSnapshot>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        return await context.GeneralSupports
            .AsNoTracking()
            .Where(g => g.IsActive)
            .OrderBy(g => g.Name)
            .Select(g => new GeneralSupportSnapshot(g.Id.Value, g.Name))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsActiveAsync(Guid generalSupportId, CancellationToken cancellationToken = default)
    {
        var typedId = GeneralSupportId.From(generalSupportId);
        return context.GeneralSupports.AsNoTracking().AnyAsync(g => g.Id == typedId && g.IsActive, cancellationToken);
    }
}
