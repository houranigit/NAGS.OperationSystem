using Core.Contracts.Features.ManpowerType;
using Core.Contracts.Readers;
using Core.Domain.Aggregates.ManpowerType;
using Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Core.Infrastructure.Readers;

internal sealed class ManpowerTypeReader(CoreDbContext context) : IManpowerTypeReader
{
    public async Task<ManpowerTypeSnapshot?> GetByIdAsync(
        Guid manpowerTypeId,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.ManpowerTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == ManpowerTypeId.From(manpowerTypeId), cancellationToken);

        return entity is null
            ? null
            : new ManpowerTypeSnapshot(entity.Id.Value, entity.Name);
    }

    public async Task<IReadOnlyList<ManpowerTypeSnapshot>> GetManyAsync(
        IReadOnlyList<Guid> manpowerTypeIds,
        CancellationToken cancellationToken = default)
    {
        if (manpowerTypeIds.Count == 0)
            return [];

        var typedIds = manpowerTypeIds.Select(ManpowerTypeId.From).ToList();

        var entities = await context.ManpowerTypes
            .AsNoTracking()
            .Where(m => typedIds.Contains(m.Id))
            .ToListAsync(cancellationToken);

        return entities
            .Select(m => new ManpowerTypeSnapshot(m.Id.Value, m.Name))
            .ToList();
    }

    public Task<bool> ExistsActiveAsync(Guid manpowerTypeId, CancellationToken cancellationToken = default)
    {
        var typedId = ManpowerTypeId.From(manpowerTypeId);
        return context.ManpowerTypes
            .AsNoTracking()
            .AnyAsync(m => m.Id == typedId && m.IsActive, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetInactiveOrMissingIdsAsync(
        IReadOnlyList<Guid> manpowerTypeIds,
        CancellationToken cancellationToken = default)
    {
        if (manpowerTypeIds.Count == 0)
            return [];

        var typedIds = manpowerTypeIds.Select(ManpowerTypeId.From).ToList();
        var activeIds = await context.ManpowerTypes
            .AsNoTracking()
            .Where(m => typedIds.Contains(m.Id) && m.IsActive)
            .Select(m => m.Id.Value)
            .ToListAsync(cancellationToken);

        var activeSet = activeIds.ToHashSet();
        return manpowerTypeIds.Where(id => !activeSet.Contains(id)).ToList();
    }
}
