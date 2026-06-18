using Core.Domain.Aggregates.Country;
using Core.Domain.Aggregates.Station;
using Core.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Core.Infrastructure.Persistence.Repositories;

public sealed class StationRepository(CoreDbContext context) : IStationRepository
{
    public async Task<Station?> GetByIdAsync(StationId id, CancellationToken ct = default) =>
        await context.Stations.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Station?> GetByIataCodeAsync(string iataCode, CancellationToken ct = default)
    {
        var parsed = AirportCode.Create(iataCode);
        if (parsed.IsFailure) return null;
        var code = parsed.Value.Value;
        return await context.Stations.FirstOrDefaultAsync(x => x.IataCode.Value == code, ct);
    }

    public async Task<IReadOnlyList<Station>> GetAllAsync(CancellationToken ct = default) =>
        await context.Stations.ToListAsync(ct);

    public async Task<IReadOnlyList<Station>> GetAllActiveAsync(CancellationToken ct = default) =>
        await context.Stations.Where(x => x.IsActive).ToListAsync(ct);

    public async Task<IReadOnlyList<Station>> GetByCountryAsync(CountryId countryId, CancellationToken ct = default) =>
        await context.Stations.Where(x => x.CountryId == countryId).ToListAsync(ct);

    public async Task<bool> ExistsByIataCodeAsync(string iataCode, CancellationToken ct = default)
    {
        var parsed = AirportCode.Create(iataCode);
        if (parsed.IsFailure) return false;
        var code = parsed.Value.Value;
        return await context.Stations.AnyAsync(x => x.IataCode.Value == code, ct);
    }

    public void Add(Station station) => context.Stations.Add(station);
    public void Update(Station station) => context.Stations.Update(station);
}
