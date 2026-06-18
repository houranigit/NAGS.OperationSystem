namespace Store.Domain.Aggregates.Unit;

public interface IUnitRepository
{
    Task<Unit?> GetByIdAsync(UnitId id, CancellationToken ct = default);
    Task<bool> ExistsByCodeAsync(string code, UnitId? excludeId = null, CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(string name, UnitId? excludeId = null, CancellationToken ct = default);
    Task<bool> ExistsActiveByIdAsync(UnitId id, CancellationToken ct = default);
    void Add(Unit unit);
    void Update(Unit unit);
}
