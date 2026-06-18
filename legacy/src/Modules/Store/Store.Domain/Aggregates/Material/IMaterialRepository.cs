namespace Store.Domain.Aggregates.Material;

public interface IMaterialRepository
{
    Task<Material?> GetByIdAsync(MaterialId id, CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(string name, MaterialId? excludeId = null, CancellationToken ct = default);
    void Add(Material material);
    void Update(Material material);
}
