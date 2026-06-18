using Microsoft.EntityFrameworkCore;
using Store.Domain.Aggregates.Material;
using Store.Domain.Aggregates.MaterialPricePlan;

namespace Store.Infrastructure.Persistence.Repositories;

public sealed class MaterialPricePlanRepository(StoreDbContext context) : IMaterialPricePlanRepository
{
    public async Task<MaterialPricePlan?> GetByIdAsync(MaterialPricePlanId id, CancellationToken ct = default) =>
        await context.MaterialPricePlans
            .Include(x => x.Brackets)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<bool> ExistsForMaterialAsync(MaterialId materialId, MaterialPricePlanId? excludeId = null, CancellationToken ct = default) =>
        await context.MaterialPricePlans.AnyAsync(
            x => x.MaterialId == materialId && (excludeId == null || x.Id != excludeId), ct);

    public async Task<bool> HasActiveForCurrencyAsync(Guid currencyId, CancellationToken ct = default) =>
        await context.MaterialPricePlans.AnyAsync(x => x.CurrencyId == currencyId && x.IsActive, ct);

    public void Add(MaterialPricePlan plan) => context.MaterialPricePlans.Add(plan);
    public void Update(MaterialPricePlan plan) => context.MaterialPricePlans.Update(plan);
    public void Remove(MaterialPricePlan plan) => context.MaterialPricePlans.Remove(plan);
}
