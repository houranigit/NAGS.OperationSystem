using Microsoft.EntityFrameworkCore;
using Store.Domain.Aggregates.GeneralSupport;
using Store.Domain.Aggregates.GeneralSupportPricePlan;

namespace Store.Infrastructure.Persistence.Repositories;

public sealed class GeneralSupportPricePlanRepository(StoreDbContext context) : IGeneralSupportPricePlanRepository
{
    public async Task<GeneralSupportPricePlan?> GetByIdAsync(GeneralSupportPricePlanId id, CancellationToken ct = default) =>
        await context.GeneralSupportPricePlans
            .Include(x => x.Brackets)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<bool> ExistsForGeneralSupportAsync(GeneralSupportId generalSupportId, GeneralSupportPricePlanId? excludeId = null, CancellationToken ct = default) =>
        await context.GeneralSupportPricePlans.AnyAsync(
            x => x.GeneralSupportId == generalSupportId && (excludeId == null || x.Id != excludeId), ct);

    public async Task<bool> HasActiveForCurrencyAsync(Guid currencyId, CancellationToken ct = default) =>
        await context.GeneralSupportPricePlans.AnyAsync(x => x.CurrencyId == currencyId && x.IsActive, ct);

    public void Add(GeneralSupportPricePlan plan) => context.GeneralSupportPricePlans.Add(plan);
    public void Update(GeneralSupportPricePlan plan) => context.GeneralSupportPricePlans.Update(plan);
    public void Remove(GeneralSupportPricePlan plan) => context.GeneralSupportPricePlans.Remove(plan);
}
