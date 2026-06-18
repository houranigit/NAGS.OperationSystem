using Microsoft.EntityFrameworkCore;
using Store.Domain.Aggregates.GeneralSupport;

namespace Store.Infrastructure.Persistence.Repositories;

public sealed class GeneralSupportRepository(StoreDbContext context) : IGeneralSupportRepository
{
    public async Task<GeneralSupport?> GetByIdAsync(GeneralSupportId id, CancellationToken ct = default) =>
        await context.GeneralSupports.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<bool> ExistsByNameAsync(string name, GeneralSupportId? excludeId = null, CancellationToken ct = default)
    {
        var trimmed = name.Trim();
        return await context.GeneralSupports.AnyAsync(
            x => x.Name == trimmed && (excludeId == null || x.Id != excludeId), ct);
    }

    public void Add(GeneralSupport item) => context.GeneralSupports.Add(item);
    public void Update(GeneralSupport item) => context.GeneralSupports.Update(item);
}
