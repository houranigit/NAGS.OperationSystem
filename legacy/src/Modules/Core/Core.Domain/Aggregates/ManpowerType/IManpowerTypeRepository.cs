namespace Core.Domain.Aggregates.ManpowerType;

public interface IManpowerTypeRepository
{
    Task<ManpowerType?> GetByIdAsync(ManpowerTypeId id, CancellationToken ct = default);
    Task<IReadOnlyList<ManpowerType>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ManpowerType>> GetAllActiveAsync(CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default);
    void Add(ManpowerType manpowerType);
    void Update(ManpowerType manpowerType);
}
