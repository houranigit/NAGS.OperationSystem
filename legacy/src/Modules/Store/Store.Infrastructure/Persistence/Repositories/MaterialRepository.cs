using Microsoft.EntityFrameworkCore;
using Store.Domain.Aggregates.Material;

namespace Store.Infrastructure.Persistence.Repositories;

public sealed class MaterialRepository(StoreDbContext context) : IMaterialRepository
{
    public async Task<Material?> GetByIdAsync(MaterialId id, CancellationToken ct = default) =>
        await context.Materials.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<bool> ExistsByNameAsync(string name, MaterialId? excludeId = null, CancellationToken ct = default)
    {
        var trimmed = name.Trim();
        return await context.Materials.AnyAsync(
            x => x.Name == trimmed && (excludeId == null || x.Id != excludeId), ct);
    }

    public void Add(Material material) => context.Materials.Add(material);
    public void Update(Material material) => context.Materials.Update(material);
}
