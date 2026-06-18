using Core.Domain.Aggregates.ManpowerType;
using Microsoft.EntityFrameworkCore;

namespace Core.Infrastructure.Persistence.Repositories;

public sealed class ManpowerTypeRepository(CoreDbContext context) : IManpowerTypeRepository
{
    public async Task<ManpowerType?> GetByIdAsync(ManpowerTypeId id, CancellationToken ct = default) =>
        await context.ManpowerTypes.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<ManpowerType>> GetAllAsync(CancellationToken ct = default) =>
        await context.ManpowerTypes.ToListAsync(ct);

    public async Task<IReadOnlyList<ManpowerType>> GetAllActiveAsync(CancellationToken ct = default) =>
        await context.ManpowerTypes.Where(x => x.IsActive).ToListAsync(ct);

    public async Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default)
    {
        var trimmed = name.Trim();
        return await context.ManpowerTypes.AnyAsync(x => x.Name == trimmed, ct);
    }

    public void Add(ManpowerType manpowerType) => context.ManpowerTypes.Add(manpowerType);
    public void Update(ManpowerType manpowerType) => context.ManpowerTypes.Update(manpowerType);
}
