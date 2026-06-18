using Core.Contracts.Features.Service;
using Core.Contracts.Readers;
using Core.Contracts.Seeding;
using Core.Domain.Aggregates.Service;
using Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Core.Infrastructure.Readers;

internal sealed class ServiceReader(CoreDbContext context) : IServiceReader
{
    public async Task<ServiceSnapshot?> GetByIdAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        var entity = await context.Services
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == ServiceId.From(serviceId), cancellationToken);

        return entity is null
            ? null
            : new ServiceSnapshot(entity.Id.Value, entity.Name, entity.Id.Value == CoreSeedIds.AogService);
    }

    public async Task<IReadOnlyList<ServiceSnapshot>> GetManyAsync(
        IReadOnlyList<Guid> serviceIds,
        CancellationToken cancellationToken = default)
    {
        if (serviceIds.Count == 0)
            return [];

        var typedIds = serviceIds.Select(ServiceId.From).ToList();

        var entities = await context.Services
            .AsNoTracking()
            .Where(s => typedIds.Contains(s.Id))
            .ToListAsync(cancellationToken);

        return entities
            .Select(s => new ServiceSnapshot(s.Id.Value, s.Name, s.Id.Value == CoreSeedIds.AogService))
            .ToList();
    }

    public async Task<IReadOnlyList<ServiceSnapshot>> ListActiveAsync(
        bool excludeAog = false,
        CancellationToken cancellationToken = default)
    {
        var query = context.Services
            .AsNoTracking()
            .Where(s => s.IsActive);

        // SQL-level AOG exclusion — the seed id is wrapped in ServiceId so the
        // comparison happens on the keyed column. Keeps the AOG row out of the
        // result set entirely instead of materialising it then filtering in memory.
        if (excludeAog)
        {
            var aogId = ServiceId.From(CoreSeedIds.AogService);
            query = query.Where(s => s.Id != aogId);
        }

        return await query
            .OrderBy(s => s.Name)
            .Select(s => new ServiceSnapshot(s.Id.Value, s.Name, s.Id.Value == CoreSeedIds.AogService))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsActiveAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        var typedId = ServiceId.From(serviceId);
        return context.Services
            .AsNoTracking()
            .AnyAsync(s => s.Id == typedId && s.IsActive, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetInactiveOrMissingIdsAsync(
        IReadOnlyList<Guid> serviceIds,
        CancellationToken cancellationToken = default)
    {
        if (serviceIds.Count == 0)
            return [];

        var typedIds = serviceIds.Select(ServiceId.From).ToList();
        var activeIds = await context.Services
            .AsNoTracking()
            .Where(s => typedIds.Contains(s.Id) && s.IsActive)
            .Select(s => s.Id.Value)
            .ToListAsync(cancellationToken);

        var activeSet = activeIds.ToHashSet();
        return serviceIds.Where(id => !activeSet.Contains(id)).ToList();
    }
}
