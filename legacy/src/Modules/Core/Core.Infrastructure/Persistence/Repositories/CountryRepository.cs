using Core.Domain.Aggregates.Country;
using Core.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Core.Infrastructure.Persistence.Repositories;

public sealed class CountryRepository(CoreDbContext context) : ICountryRepository
{
    public async Task<Country?> GetByIdAsync(CountryId id, CancellationToken ct = default) =>
        await context.Countries.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Country?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var parsed = CountryCode.Create(code);
        if (parsed.IsFailure) return null;
        var value = parsed.Value.Value;
        return await context.Countries.FirstOrDefaultAsync(x => x.Code.Value == value, ct);
    }

    public async Task<IReadOnlyList<Country>> GetAllAsync(CancellationToken ct = default) =>
        await context.Countries.ToListAsync(ct);

    public async Task<IReadOnlyList<Country>> GetAllActiveAsync(CancellationToken ct = default) =>
        await context.Countries.Where(x => x.IsActive).ToListAsync(ct);

    public async Task<bool> ExistsByCodeAsync(string code, CancellationToken ct = default)
    {
        var parsed = CountryCode.Create(code);
        if (parsed.IsFailure) return false;
        var value = parsed.Value.Value;
        return await context.Countries.AnyAsync(x => x.Code.Value == value, ct);
    }

    public void Add(Country country) => context.Countries.Add(country);
    public void Update(Country country) => context.Countries.Update(country);
}
