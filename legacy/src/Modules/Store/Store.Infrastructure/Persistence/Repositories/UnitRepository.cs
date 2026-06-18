using Microsoft.EntityFrameworkCore;
using Store.Domain.Aggregates.Unit;

namespace Store.Infrastructure.Persistence.Repositories;

public sealed class UnitRepository(StoreDbContext context) : IUnitRepository
{
    public async Task<Unit?> GetByIdAsync(UnitId id, CancellationToken ct = default) =>
        await context.Units.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<bool> ExistsByCodeAsync(string code, UnitId? excludeId = null, CancellationToken ct = default)
    {
        var trimmed = code.Trim().ToUpperInvariant();
        return await context.Units.AnyAsync(
            x => x.Code == trimmed && (excludeId == null || x.Id != excludeId), ct);
    }

    public async Task<bool> ExistsByNameAsync(string name, UnitId? excludeId = null, CancellationToken ct = default)
    {
        var trimmed = name.Trim();
        return await context.Units.AnyAsync(
            x => x.Name == trimmed && (excludeId == null || x.Id != excludeId), ct);
    }

    public async Task<bool> ExistsActiveByIdAsync(UnitId id, CancellationToken ct = default) =>
        await context.Units.AnyAsync(x => x.Id == id && x.IsActive, ct);

    public void Add(Unit unit) => context.Units.Add(unit);
    public void Update(Unit unit) => context.Units.Update(unit);
}
