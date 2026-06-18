using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Contracts.Features.Currency;
using Core.Domain.Aggregates.Currency;
using Microsoft.EntityFrameworkCore;

namespace Core.Application.Features.Currency.Queries.GetExchangeRatesForCurrency;

/// <summary>
/// Returns every exchange-rate row that touches <see cref="GetExchangeRatesForCurrencyQuery.CurrencyId"/>
/// — both directions, so the wizard can translate plan currency → contract currency.
/// </summary>
/// <remarks>
/// Stays on <see cref="IQueryable{T}"/> until the terminal <c>ToListAsync</c>; the projection
/// flattens the strongly-typed <c>CurrencyId</c> VOs to raw <see cref="Guid"/>s for transport.
/// </remarks>
public sealed class GetExchangeRatesForCurrencyQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetExchangeRatesForCurrencyQuery, IReadOnlyList<ExchangeRateRowDto>>
{
    public async Task<Result<IReadOnlyList<ExchangeRateRowDto>>> Handle(
        GetExchangeRatesForCurrencyQuery request,
        CancellationToken cancellationToken)
    {
        if (request.CurrencyId == Guid.Empty)
            return Error.Validation("CurrencyId is required.");

        var currencyId = CurrencyId.From(request.CurrencyId);

        var items = await db.ExchangeRates
            .Where(r => r.CurrencyId == currencyId || r.ToCurrencyId == currencyId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ExchangeRateRowDto(
                r.CurrencyId.Value,
                r.ToCurrencyId.Value,
                r.Rate,
                r.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<ExchangeRateRowDto>>.Success(items);
    }
}
