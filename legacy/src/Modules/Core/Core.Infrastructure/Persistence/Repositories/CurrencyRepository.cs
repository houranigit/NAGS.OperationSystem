using Core.Domain.Aggregates.Currency;
using Core.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Core.Infrastructure.Persistence.Repositories;

public sealed class CurrencyRepository(CoreDbContext context) : ICurrencyRepository
{
    public async Task<Currency?> GetByIdAsync(CurrencyId id, CancellationToken ct = default) =>
        await context.Currencies.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Currency?> GetByIdWithRatesAsync(CurrencyId id, CancellationToken ct = default) =>
        await context.Currencies
            .Include(x => x.ExchangeRates)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Currency?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var parsed = CurrencyCode.Create(code);
        if (parsed.IsFailure) return null;
        var value = parsed.Value.Value;
        return await context.Currencies.FirstOrDefaultAsync(x => x.Code.Value == value, ct);
    }

    public async Task<IReadOnlyList<Currency>> GetAllAsync(CancellationToken ct = default) =>
        await context.Currencies.ToListAsync(ct);

    public async Task<IReadOnlyList<Currency>> GetAllActiveAsync(CancellationToken ct = default) =>
        await context.Currencies.Where(x => x.IsActive).ToListAsync(ct);

    public async Task<bool> ExistsByCodeAsync(string code, CancellationToken ct = default)
    {
        var parsed = CurrencyCode.Create(code);
        if (parsed.IsFailure) return false;
        var value = parsed.Value.Value;
        return await context.Currencies.AnyAsync(x => x.Code.Value == value, ct);
    }

    public async Task<ExchangeRate?> GetCurrentRateAsync(CurrencyId fromId, CurrencyId toId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await context.ExchangeRates
            .Where(x => x.CurrencyId == fromId && x.ToCurrencyId == toId &&
                         (x.UpdatedAt ?? x.CreatedAt) <= now)
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ExchangeRate?> GetRateOnDateAsync(CurrencyId fromId, CurrencyId toId, DateTime date, CancellationToken ct = default) =>
        await context.ExchangeRates
            .Where(x => x.CurrencyId == fromId && x.ToCurrencyId == toId &&
                         (x.UpdatedAt ?? x.CreatedAt) <= date)
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ExchangeRate>> GetRateHistoryAsync(CurrencyId fromId, CurrencyId toId, CancellationToken ct = default) =>
        await context.ExchangeRates
            .Where(x => x.CurrencyId == fromId && x.ToCurrencyId == toId)
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .ToListAsync(ct);

    public void Add(Currency currency) => context.Currencies.Add(currency);
    public void Update(Currency currency) => context.Currencies.Update(currency);
}
