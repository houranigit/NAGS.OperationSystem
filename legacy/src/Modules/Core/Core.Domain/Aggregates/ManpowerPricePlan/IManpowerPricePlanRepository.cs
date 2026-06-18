using Core.Domain.Aggregates.Currency;
using Core.Domain.Aggregates.ManpowerType;
using Core.Domain.Aggregates.OperationType;

namespace Core.Domain.Aggregates.ManpowerPricePlan;

public interface IManpowerPricePlanRepository
{
    Task<ManpowerPricePlan?> GetByIdAsync(ManpowerPricePlanId id, CancellationToken ct = default);

    Task<bool> ExistsForCombinationAsync(
        ManpowerTypeId manpowerTypeId,
        OperationTypeId operationTypeId,
        ManpowerPricePlanId? excludeId = null,
        CancellationToken ct = default);

    Task<bool> HasActiveForManpowerTypeAsync(ManpowerTypeId manpowerTypeId, CancellationToken ct = default);
    Task<bool> HasActiveForOperationTypeAsync(OperationTypeId operationTypeId, CancellationToken ct = default);
    Task<bool> HasActiveForCurrencyAsync(CurrencyId currencyId, CancellationToken ct = default);

    void Add(ManpowerPricePlan plan);
    void Update(ManpowerPricePlan plan);
    void Remove(ManpowerPricePlan plan);
}
