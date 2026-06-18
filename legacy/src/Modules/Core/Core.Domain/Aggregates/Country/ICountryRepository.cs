namespace Core.Domain.Aggregates.Country;

public interface ICountryRepository
{
    Task<Country?> GetByIdAsync(CountryId id, CancellationToken ct = default);
    Task<Country?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<Country>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Country>> GetAllActiveAsync(CancellationToken ct = default);
    Task<bool> ExistsByCodeAsync(string code, CancellationToken ct = default);
    void Add(Country country);
    void Update(Country country);
}
