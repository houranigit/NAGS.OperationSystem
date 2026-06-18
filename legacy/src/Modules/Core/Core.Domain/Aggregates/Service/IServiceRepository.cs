namespace Core.Domain.Aggregates.Service;

public interface IServiceRepository
{
    Task<Service?> GetByIdAsync(ServiceId id, CancellationToken ct = default);
    Task<IReadOnlyList<Service>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Service>> GetAllActiveAsync(CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default);
    void Add(Service service);
    void Update(Service service);
}
