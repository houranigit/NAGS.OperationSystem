using Microsoft.EntityFrameworkCore;
using Store.Contracts.Features.Material;
using Store.Contracts.Readers;
using Store.Domain.Aggregates.Material;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Readers;

internal sealed class MaterialReader(StoreDbContext context) : IMaterialReader
{
    public async Task<MaterialSnapshot?> GetByIdAsync(Guid materialId, CancellationToken cancellationToken = default)
    {
        var entity = await context.Materials
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == MaterialId.From(materialId), cancellationToken);

        return entity is null ? null : new MaterialSnapshot(entity.Id.Value, entity.Name);
    }

    public async Task<IReadOnlyList<MaterialSnapshot>> GetManyAsync(
        IReadOnlyList<Guid> materialIds,
        CancellationToken cancellationToken = default)
    {
        if (materialIds.Count == 0) return [];

        var typedIds = materialIds.Select(MaterialId.From).ToList();
        var entities = await context.Materials
            .AsNoTracking()
            .Where(m => typedIds.Contains(m.Id))
            .ToListAsync(cancellationToken);

        return entities.Select(m => new MaterialSnapshot(m.Id.Value, m.Name)).ToList();
    }

    public async Task<IReadOnlyList<MaterialSnapshot>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        return await context.Materials
            .AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.Name)
            .Select(m => new MaterialSnapshot(m.Id.Value, m.Name))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsActiveAsync(Guid materialId, CancellationToken cancellationToken = default)
    {
        var typedId = MaterialId.From(materialId);
        return context.Materials.AsNoTracking().AnyAsync(m => m.Id == typedId && m.IsActive, cancellationToken);
    }
}
