using Core.Domain.Aggregates.AircraftType;
using Core.Domain.Enumerations;
using Microsoft.EntityFrameworkCore;

namespace Core.Infrastructure.Persistence.Repositories;

public sealed class AircraftTypeRepository(CoreDbContext context) : IAircraftTypeRepository
{
    public async Task<AircraftType?> GetByIdAsync(AircraftTypeId id, CancellationToken ct = default) =>
        await context.AircraftTypes.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<AircraftType>> GetAllAsync(CancellationToken ct = default) =>
        await context.AircraftTypes.ToListAsync(ct);

    public async Task<IReadOnlyList<AircraftType>> GetAllActiveAsync(CancellationToken ct = default) =>
        await context.AircraftTypes.Where(x => x.IsActive).ToListAsync(ct);

    public async Task<bool> ExistsByManufacturerAndModelAsync(Manufacturer manufacturer, string model, AircraftTypeId? excludeId = null, CancellationToken ct = default)
    {
        var normalized = model.Trim().ToUpperInvariant();
        return await context.AircraftTypes.AnyAsync(
            x => x.Manufacturer == manufacturer && x.Model == normalized && (excludeId == null || x.Id != excludeId),
            ct);
    }

    public void Add(AircraftType aircraftType) => context.AircraftTypes.Add(aircraftType);
    public void Update(AircraftType aircraftType) => context.AircraftTypes.Update(aircraftType);
}
