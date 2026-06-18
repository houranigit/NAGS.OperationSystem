using Core.Domain.Aggregates.AircraftType;
using Core.Domain.Aggregates.Currency;
using Core.Domain.Aggregates.OperationType;
using Core.Domain.Aggregates.Service;
using Core.Domain.Aggregates.ServicePricePlan;
using Microsoft.EntityFrameworkCore;

namespace Core.Infrastructure.Persistence.Repositories;

public sealed class ServicePricePlanRepository(CoreDbContext context) : IServicePricePlanRepository
{
    public async Task<ServicePricePlan?> GetByIdAsync(ServicePricePlanId id, CancellationToken ct = default) =>
        await context.ServicePricePlans
            .Include(x => x.Brackets)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<bool> ExistsForCombinationAsync(
        ServiceId serviceId,
        OperationTypeId operationTypeId,
        AircraftTypeId? aircraftTypeId,
        ServicePricePlanId? excludeId = null,
        CancellationToken ct = default)
    {
        if (aircraftTypeId is null)
        {
            return await context.ServicePricePlans.AnyAsync(
                x => x.ServiceId == serviceId
                     && x.OperationTypeId == operationTypeId
                     && x.AircraftTypeId == null
                     && (excludeId == null || x.Id != excludeId),
                ct);
        }

        return await context.ServicePricePlans.AnyAsync(
            x => x.ServiceId == serviceId
                 && x.OperationTypeId == operationTypeId
                 && x.AircraftTypeId == aircraftTypeId
                 && (excludeId == null || x.Id != excludeId),
            ct);
    }

    public async Task<bool> HasActiveForServiceAsync(ServiceId serviceId, CancellationToken ct = default) =>
        await context.ServicePricePlans.AnyAsync(x => x.ServiceId == serviceId && x.IsActive, ct);

    public async Task<bool> HasActiveForOperationTypeAsync(OperationTypeId operationTypeId, CancellationToken ct = default) =>
        await context.ServicePricePlans.AnyAsync(x => x.OperationTypeId == operationTypeId && x.IsActive, ct);

    public async Task<bool> HasActiveForAircraftTypeAsync(AircraftTypeId aircraftTypeId, CancellationToken ct = default) =>
        await context.ServicePricePlans.AnyAsync(x => x.AircraftTypeId == aircraftTypeId && x.IsActive, ct);

    public async Task<bool> HasActiveForCurrencyAsync(CurrencyId currencyId, CancellationToken ct = default) =>
        await context.ServicePricePlans.AnyAsync(x => x.CurrencyId == currencyId && x.IsActive, ct);

    public void Add(ServicePricePlan plan) => context.ServicePricePlans.Add(plan);
    public void Update(ServicePricePlan plan) => context.ServicePricePlans.Update(plan);
    public void Remove(ServicePricePlan plan) => context.ServicePricePlans.Remove(plan);
}
