using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Application.Features.Customer.Queries.GetPaginatedCustomers;
using Core.Contracts.Features.Currency;
using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;

namespace Core.Application.Features.Currency.Queries.GetPaginatedCurrencies;

/// <summary>
/// Paginated grid for currencies (with nested exchange-rate rows). Same pipeline as
/// <see cref="GetPaginatedCustomersQueryHandler"/>.
/// </summary>
/// <remarks>
/// <para><b>Wrong:</b> materializing <c>Currencies</c> / <c>ExchangeRates</c> with <c>ToListAsync</c> then paging in memory.</para>
/// <para><b>Right:</b> filter → count → order → <c>Skip</c>/<c>Take</c> → <c>Select</c> with nested <c>ExchangeRates</c> (and <c>TargetCurrency</c>) → one <c>ToListAsync</c>. Avoid eager <c>Include</c> on rates when ordering nested rows: <c>OrderBy(r =&gt; r.Id.Value)</c> is not translatable.</para>
/// </remarks>
public sealed class GetPaginatedCurrenciesQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedCurrenciesQuery, PaginatedResult<CurrencyDto>>
{
    public async Task<Result<PaginatedResult<CurrencyDto>>> Handle(
        GetPaginatedCurrenciesQuery request,
        CancellationToken cancellationToken)
    {
        // Do not Include children here: paging + projection use c.ExchangeRates in Select alone.
        // Include + nested OrderBy on strongly-typed IDs (r.Id.Value) fails SQL translation on SQL Server.
        var query = db.Currencies.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Code.Value);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(c => new CurrencyDto(
                c.Id.Value,
                c.Code.Value,
                c.Name,
                c.IsActive,
                c.CreatedAt,
                c.UpdatedAt,
                c.ExchangeRates
                    .OrderBy(r => r.CreatedAt)
                    .ThenBy(r => r.Rate)
                    .Select(er =>
                        new ExchangeRateDto(
                            er.Id.Value,
                            er.ToCurrencyId.Value,
                            er.TargetCurrency != null ? er.TargetCurrency.Code.Value : string.Empty,
                            er.TargetCurrency != null ? er.TargetCurrency.Name : string.Empty,
                            er.Rate,
                            er.CreatedAt,
                            er.UpdatedAt))
                    .ToList()))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<CurrencyDto>(items, total, request.Page, request.PageSize);
    }
}
