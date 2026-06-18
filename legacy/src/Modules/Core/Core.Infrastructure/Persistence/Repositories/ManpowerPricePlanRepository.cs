using Core.Domain.Aggregates.Currency;
using Core.Domain.Aggregates.ManpowerPricePlan;
using Core.Domain.Aggregates.ManpowerType;
using Core.Domain.Aggregates.OperationType;
using Microsoft.EntityFrameworkCore;

namespace Core.Infrastructure.Persistence.Repositories;

public sealed class ManpowerPricePlanRepository(CoreDbContext context) : IManpowerPricePlanRepository
{
    public async Task<ManpowerPricePlan?> GetByIdAsync(ManpowerPricePlanId id, CancellationToken ct = default) =>
        await context.ManpowerPricePlans
            .Include(x => x.Brackets)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<bool> ExistsForCombinationAsync(
        ManpowerTypeId manpowerTypeId,
        OperationTypeId operationTypeId,
        ManpowerPricePlanId? excludeId = null,
        CancellationToken ct = default) =>
        await context.ManpowerPricePlans.AnyAsync(
            x => x.ManpowerTypeId == manpowerTypeId
                 && x.OperationTypeId == operationTypeId
                 && (excludeId == null || x.Id != excludeId),
            ct);

    public async Task<bool> HasActiveForManpowerTypeAsync(ManpowerTypeId manpowerTypeId, CancellationToken ct = default) =>
        await context.ManpowerPricePlans.AnyAsync(x => x.ManpowerTypeId == manpowerTypeId && x.IsActive, ct);

    public async Task<bool> HasActiveForOperationTypeAsync(OperationTypeId operationTypeId, CancellationToken ct = default) =>
        await context.ManpowerPricePlans.AnyAsync(x => x.OperationTypeId == operationTypeId && x.IsActive, ct);

    public async Task<bool> HasActiveForCurrencyAsync(CurrencyId currencyId, CancellationToken ct = default) =>
        await context.ManpowerPricePlans.AnyAsync(x => x.CurrencyId == currencyId && x.IsActive, ct);

    public void Add(ManpowerPricePlan plan) => context.ManpowerPricePlans.Add(plan);
    public void Update(ManpowerPricePlan plan) => context.ManpowerPricePlans.Update(plan);
    public void Remove(ManpowerPricePlan plan) => context.ManpowerPricePlans.Remove(plan);
}
