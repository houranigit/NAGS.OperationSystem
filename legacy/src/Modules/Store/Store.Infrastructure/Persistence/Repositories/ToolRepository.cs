using Microsoft.EntityFrameworkCore;
using Store.Domain.Aggregates.Tool;

namespace Store.Infrastructure.Persistence.Repositories;

public sealed class ToolRepository(StoreDbContext context) : IToolRepository
{
    public async Task<Tool?> GetByIdAsync(ToolId id, CancellationToken ct = default) =>
        await context.Tools
            .Include(x => x.Equipments)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<bool> ExistsByNameAsync(string name, ToolId? excludeId = null, CancellationToken ct = default)
    {
        var trimmed = name.Trim();
        return await context.Tools.AnyAsync(
            x => x.Name == trimmed && (excludeId == null || x.Id != excludeId), ct);
    }

    public void Add(Tool tool) => context.Tools.Add(tool);
    public void Update(Tool tool) => context.Tools.Update(tool);
}
