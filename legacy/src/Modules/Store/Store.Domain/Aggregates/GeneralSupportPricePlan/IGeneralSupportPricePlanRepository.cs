using Store.Domain.Aggregates.GeneralSupport;

namespace Store.Domain.Aggregates.GeneralSupportPricePlan;

public interface IGeneralSupportPricePlanRepository
{
    Task<GeneralSupportPricePlan?> GetByIdAsync(GeneralSupportPricePlanId id, CancellationToken ct = default);
    Task<bool> ExistsForGeneralSupportAsync(GeneralSupportId generalSupportId, GeneralSupportPricePlanId? excludeId = null, CancellationToken ct = default);
    Task<bool> HasActiveForCurrencyAsync(Guid currencyId, CancellationToken ct = default);
    void Add(GeneralSupportPricePlan plan);
    void Update(GeneralSupportPricePlan plan);
    void Remove(GeneralSupportPricePlan plan);
}
