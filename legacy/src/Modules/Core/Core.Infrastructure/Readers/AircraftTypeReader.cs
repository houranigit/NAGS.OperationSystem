using Core.Contracts.Features.AircraftType;
using Core.Contracts.Readers;
using Core.Domain.Aggregates.AircraftType;
using Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Core.Infrastructure.Readers;

internal sealed class AircraftTypeReader(CoreDbContext context) : IAircraftTypeReader
{
    public async Task<AircraftTypeSnapshot?> GetByIdAsync(
        Guid aircraftTypeId,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.AircraftTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == AircraftTypeId.From(aircraftTypeId), cancellationToken);

        return entity is null
            ? null
            : new AircraftTypeSnapshot(entity.Id.Value, entity.Model);
    }

    public async Task<IReadOnlyList<AircraftTypeSnapshot>> ListActiveAsync(
        CancellationToken cancellationToken = default) =>
        await context.AircraftTypes
            .AsNoTracking()
            .Where(a => a.IsActive)
            .OrderBy(a => a.Model)
            .Select(a => new AircraftTypeSnapshot(a.Id.Value, a.Model))
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsActiveAsync(Guid aircraftTypeId, CancellationToken cancellationToken = default)
    {
        var typedId = AircraftTypeId.From(aircraftTypeId);
        return context.AircraftTypes
            .AsNoTracking()
            .AnyAsync(a => a.Id == typedId && a.IsActive, cancellationToken);
    }
}
