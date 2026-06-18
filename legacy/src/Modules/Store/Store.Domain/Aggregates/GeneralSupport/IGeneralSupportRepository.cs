namespace Store.Domain.Aggregates.GeneralSupport;

public interface IGeneralSupportRepository
{
    Task<GeneralSupport?> GetByIdAsync(GeneralSupportId id, CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(string name, GeneralSupportId? excludeId = null, CancellationToken ct = default);
    void Add(GeneralSupport item);
    void Update(GeneralSupport item);
}
