namespace Identity.Domain.Aggregates.Role;

public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(RoleId id, CancellationToken ct = default);
    Task<IReadOnlyList<Role>> GetByIdsAsync(IEnumerable<RoleId> ids, CancellationToken ct = default);
    Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken ct = default);
    Task<Role?> GetByNameAsync(string name, CancellationToken ct = default);
    void Add(Role role);
    void Update(Role role);
    void Remove(Role role);
}
