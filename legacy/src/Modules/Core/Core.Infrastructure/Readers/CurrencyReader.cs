using Core.Contracts.Features.Currency;
using Core.Contracts.Readers;
using Core.Domain.Aggregates.Currency;
using Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Core.Infrastructure.Readers;

internal sealed class CurrencyReader(CoreDbContext context) : ICurrencyReader
{
    public async Task<CurrencySnapshot?> GetByIdAsync(Guid currencyId, CancellationToken cancellationToken = default)
    {
        var entity = await context.Currencies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == CurrencyId.From(currencyId), cancellationToken);

        return entity is null ? null : new CurrencySnapshot(entity.Id.Value, entity.Code.Value);
    }

    public Task<bool> ExistsActiveAsync(Guid currencyId, CancellationToken cancellationToken = default)
    {
        var typedId = CurrencyId.From(currencyId);
        return context.Currencies
            .AsNoTracking()
            .AnyAsync(c => c.Id == typedId && c.IsActive, cancellationToken);
    }

    public Task<bool> HasRateToAsync(
        Guid fromCurrencyId,
        Guid toCurrencyId,
        DateTime onDate,
        CancellationToken cancellationToken = default)
    {
        // Same-currency conversion is always trivially available (rate 1.0). Treat as satisfied
        // so platform-currency contracts do not require a self-loop exchange rate row.
        if (fromCurrencyId == toCurrencyId)
            return Task.FromResult(true);

        var fromId = CurrencyId.From(fromCurrencyId);
        var toId = CurrencyId.From(toCurrencyId);
        return context.ExchangeRates
            .AsNoTracking()
            .AnyAsync(
                r => r.CurrencyId == fromId && r.ToCurrencyId == toId && r.CreatedAt <= onDate,
                cancellationToken);
    }
}
