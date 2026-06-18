using Store.Domain.Aggregates.Tool;

namespace Store.Domain.Aggregates.ToolPricePlan;

public interface IToolPricePlanRepository
{
    Task<ToolPricePlan?> GetByIdAsync(ToolPricePlanId id, CancellationToken ct = default);
    Task<bool> ExistsForToolAsync(ToolId toolId, ToolPricePlanId? excludeId = null, CancellationToken ct = default);
    Task<bool> HasActiveForCurrencyAsync(Guid currencyId, CancellationToken ct = default);
    void Add(ToolPricePlan plan);
    void Update(ToolPricePlan plan);
    void Remove(ToolPricePlan plan);
}
