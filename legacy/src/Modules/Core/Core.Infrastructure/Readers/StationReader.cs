using Core.Contracts.Features.Station;
using Core.Contracts.Readers;
using Core.Domain.Aggregates.Station;
using Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Core.Infrastructure.Readers;

internal sealed class StationReader(CoreDbContext context) : IStationReader
{
    public async Task<StationSnapshot?> GetByIdAsync(Guid stationId, CancellationToken cancellationToken = default)
    {
        var entity = await context.Stations
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == StationId.From(stationId), cancellationToken);

        return entity is null
            ? null
            : new StationSnapshot(entity.Id.Value, entity.Name, entity.IataCode.Value);
    }

    public async Task<IReadOnlyList<StationSnapshot>> GetManyAsync(
        IReadOnlyList<Guid> stationIds,
        CancellationToken cancellationToken = default)
    {
        if (stationIds.Count == 0)
            return [];

        var typedIds = stationIds.Select(StationId.From).ToList();

        var entities = await context.Stations
            .AsNoTracking()
            .Where(s => typedIds.Contains(s.Id))
            .ToListAsync(cancellationToken);

        return entities
            .Select(s => new StationSnapshot(s.Id.Value, s.Name, s.IataCode.Value))
            .ToList();
    }

    public Task<bool> ExistsActiveAsync(Guid stationId, CancellationToken cancellationToken = default)
    {
        var typedId = StationId.From(stationId);
        return context.Stations
            .AsNoTracking()
            .AnyAsync(s => s.Id == typedId && s.IsActive, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetInactiveOrMissingIdsAsync(
        IReadOnlyList<Guid> stationIds,
        CancellationToken cancellationToken = default)
    {
        if (stationIds.Count == 0)
            return [];

        var typedIds = stationIds.Select(StationId.From).ToList();
        var activeIds = await context.Stations
            .AsNoTracking()
            .Where(s => typedIds.Contains(s.Id) && s.IsActive)
            .Select(s => s.Id.Value)
            .ToListAsync(cancellationToken);

        var activeSet = activeIds.ToHashSet();
        return stationIds.Where(id => !activeSet.Contains(id)).ToList();
    }
}
