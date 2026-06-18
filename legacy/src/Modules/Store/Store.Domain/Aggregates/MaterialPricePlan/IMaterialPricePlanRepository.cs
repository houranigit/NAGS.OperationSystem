using Store.Domain.Aggregates.Material;

namespace Store.Domain.Aggregates.MaterialPricePlan;

public interface IMaterialPricePlanRepository
{
    Task<MaterialPricePlan?> GetByIdAsync(MaterialPricePlanId id, CancellationToken ct = default);
    Task<bool> ExistsForMaterialAsync(MaterialId materialId, MaterialPricePlanId? excludeId = null, CancellationToken ct = default);
    Task<bool> HasActiveForCurrencyAsync(Guid currencyId, CancellationToken ct = default);
    void Add(MaterialPricePlan plan);
    void Update(MaterialPricePlan plan);
    void Remove(MaterialPricePlan plan);
}
