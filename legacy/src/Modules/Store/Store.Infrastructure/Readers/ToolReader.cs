using Microsoft.EntityFrameworkCore;
using Store.Contracts.Features.Tool;
using Store.Contracts.Readers;
using Store.Domain.Aggregates.Tool;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Readers;

internal sealed class ToolReader(StoreDbContext context) : IToolReader
{
    public async Task<ToolSnapshot?> GetByIdAsync(Guid toolId, CancellationToken cancellationToken = default)
    {
        var entity = await context.Tools
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == ToolId.From(toolId), cancellationToken);

        return entity is null ? null : new ToolSnapshot(entity.Id.Value, entity.Name);
    }

    public async Task<IReadOnlyList<ToolSnapshot>> GetManyAsync(
        IReadOnlyList<Guid> toolIds,
        CancellationToken cancellationToken = default)
    {
        if (toolIds.Count == 0) return [];

        var typedIds = toolIds.Select(ToolId.From).ToList();
        var entities = await context.Tools
            .AsNoTracking()
            .Where(t => typedIds.Contains(t.Id))
            .ToListAsync(cancellationToken);

        return entities.Select(t => new ToolSnapshot(t.Id.Value, t.Name)).ToList();
    }

    public async Task<IReadOnlyList<ToolSnapshot>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        return await context.Tools
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new ToolSnapshot(t.Id.Value, t.Name))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsActiveAsync(Guid toolId, CancellationToken cancellationToken = default)
    {
        var typedId = ToolId.From(toolId);
        return context.Tools.AsNoTracking().AnyAsync(t => t.Id == typedId && t.IsActive, cancellationToken);
    }
}
