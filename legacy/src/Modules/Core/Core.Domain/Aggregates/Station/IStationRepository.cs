using Core.Domain.Aggregates.Country;

namespace Core.Domain.Aggregates.Station;

public interface IStationRepository
{
    Task<Station?> GetByIdAsync(StationId id, CancellationToken ct = default);
    Task<Station?> GetByIataCodeAsync(string iataCode, CancellationToken ct = default);
    Task<IReadOnlyList<Station>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Station>> GetAllActiveAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Station>> GetByCountryAsync(CountryId countryId, CancellationToken ct = default);
    Task<bool> ExistsByIataCodeAsync(string iataCode, CancellationToken ct = default);
    void Add(Station station);
    void Update(Station station);
}
