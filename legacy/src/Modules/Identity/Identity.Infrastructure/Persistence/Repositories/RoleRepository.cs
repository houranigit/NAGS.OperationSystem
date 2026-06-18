using Identity.Domain.Aggregates.Role;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence.Repositories;

public sealed class RoleRepository(IdentityDbContext context) : IRoleRepository
{
    public async Task<Role?> GetByIdAsync(RoleId id, CancellationToken ct = default) =>
        await context.Roles
            .Include("Permissions")
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Role>> GetByIdsAsync(IEnumerable<RoleId> ids, CancellationToken ct = default) =>
        await context.Roles
            .Include("Permissions")
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken ct = default) =>
        await context.Roles
            .Include("Permissions")
            .ToListAsync(ct);

    public async Task<Role?> GetByNameAsync(string name, CancellationToken ct = default) =>
        await context.Roles
            .Include("Permissions")
            .FirstOrDefaultAsync(x => x.Name == name, ct);

    public void Add(Role role) => context.Roles.Add(role);
    public void Update(Role role) => context.Roles.Update(role);
    public void Remove(Role role) => context.Roles.Remove(role);
}
