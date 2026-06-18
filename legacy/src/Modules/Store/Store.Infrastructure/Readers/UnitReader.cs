using Microsoft.EntityFrameworkCore;
using Store.Contracts.Features.Unit;
using Store.Contracts.Readers;
using Store.Domain.Aggregates.Unit;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Readers;

internal sealed class UnitReader(StoreDbContext context) : IUnitReader
{
    public async Task<UnitSnapshot?> GetByIdAsync(Guid unitId, CancellationToken cancellationToken = default)
    {
        var entity = await context.Units
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == UnitId.From(unitId), cancellationToken);

        return entity is null ? null : new UnitSnapshot(entity.Id.Value, entity.Code, entity.Name);
    }

    public async Task<IReadOnlyList<UnitSnapshot>> GetManyAsync(
        IReadOnlyList<Guid> unitIds,
        CancellationToken cancellationToken = default)
    {
        if (unitIds.Count == 0) return [];

        var typedIds = unitIds.Select(UnitId.From).ToList();
        var entities = await context.Units
            .AsNoTracking()
            .Where(u => typedIds.Contains(u.Id))
            .ToListAsync(cancellationToken);

        return entities.Select(u => new UnitSnapshot(u.Id.Value, u.Code, u.Name)).ToList();
    }

    public Task<bool> ExistsActiveAsync(Guid unitId, CancellationToken cancellationToken = default)
    {
        var typedId = UnitId.From(unitId);
        return context.Units.AsNoTracking().AnyAsync(u => u.Id == typedId && u.IsActive, cancellationToken);
    }
}
