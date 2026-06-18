using Microsoft.EntityFrameworkCore;
using Store.Domain.Aggregates.Tool;
using Store.Domain.Aggregates.ToolPricePlan;

namespace Store.Infrastructure.Persistence.Repositories;

public sealed class ToolPricePlanRepository(StoreDbContext context) : IToolPricePlanRepository
{
    public async Task<ToolPricePlan?> GetByIdAsync(ToolPricePlanId id, CancellationToken ct = default) =>
        await context.ToolPricePlans
            .Include(x => x.Brackets)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<bool> ExistsForToolAsync(ToolId toolId, ToolPricePlanId? excludeId = null, CancellationToken ct = default) =>
        await context.ToolPricePlans.AnyAsync(
            x => x.ToolId == toolId && (excludeId == null || x.Id != excludeId), ct);

    public async Task<bool> HasActiveForCurrencyAsync(Guid currencyId, CancellationToken ct = default) =>
        await context.ToolPricePlans.AnyAsync(x => x.CurrencyId == currencyId && x.IsActive, ct);

    public void Add(ToolPricePlan plan) => context.ToolPricePlans.Add(plan);
    public void Update(ToolPricePlan plan) => context.ToolPricePlans.Update(plan);
    public void Remove(ToolPricePlan plan) => context.ToolPricePlans.Remove(plan);
}
