using Core.Domain.Aggregates.Service;
using Microsoft.EntityFrameworkCore;

namespace Core.Infrastructure.Persistence.Repositories;

public sealed class ServiceRepository(CoreDbContext context) : IServiceRepository
{
    public async Task<Service?> GetByIdAsync(ServiceId id, CancellationToken ct = default) =>
        await context.Services.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Service>> GetAllAsync(CancellationToken ct = default) =>
        await context.Services.ToListAsync(ct);

    public async Task<IReadOnlyList<Service>> GetAllActiveAsync(CancellationToken ct = default) =>
        await context.Services.Where(x => x.IsActive).ToListAsync(ct);

    public async Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default)
    {
        var trimmed = name.Trim();
        return await context.Services.AnyAsync(x => x.Name == trimmed, ct);
    }

    public void Add(Service service) => context.Services.Add(service);
    public void Update(Service service) => context.Services.Update(service);
}
