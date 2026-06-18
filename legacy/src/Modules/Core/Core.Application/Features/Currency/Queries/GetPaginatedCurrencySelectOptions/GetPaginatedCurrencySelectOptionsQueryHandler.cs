using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Contracts.Features.Currency;
using Microsoft.EntityFrameworkCore;

namespace Core.Application.Features.Currency.Queries.GetPaginatedCurrencySelectOptions;

/// <summary>
/// Reference pattern for Core <c>GetPaginated*SelectOptions</c> — mirrors
/// <see cref="Customer.Queries.GetPaginatedCustomerSelectOptions.GetPaginatedCustomerSelectOptionsQueryHandler"/> (IQueryable → single <c>ToListAsync</c>).
/// </summary>
public sealed class GetPaginatedCurrencySelectOptionsQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedCurrencySelectOptionsQuery, PaginatedResult<CurrencySelectOption>>
{
    public async Task<Result<PaginatedResult<CurrencySelectOption>>> Handle(
        GetPaginatedCurrencySelectOptionsQuery request,
        CancellationToken cancellationToken)
    {
        // 1. Baseline — stay on IQueryable until the final ToListAsync.
        var query = db.Currencies.Where(x => x.IsActive).AsQueryable();

        // Contract-only restriction: keep the platform currency + any currency that already
        // has an exchange rate set to the platform currency. Billing requires that conversion
        // exists, so currencies without it cannot be picked for a new contract.
        if (request.ContractCurrencyOnly)
        {
            if (string.IsNullOrWhiteSpace(request.PlatformCurrencyCode))
                return Error.Validation("PlatformCurrencyCode is required when ContractCurrencyOnly is true.");

            var platformCode = request.PlatformCurrencyCode.Trim().ToUpperInvariant();
            query = query.Where(x =>
                x.Code.Value == platformCode
                || db.ExchangeRates.Any(r =>
                    r.CurrencyId == x.Id
                    && db.Currencies.Any(p => p.Id == r.ToCurrencyId && p.Code.Value == platformCode)));
        }

        // 2. Dynamic filters (entity property names).
        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        // 3. Total before paging.
        var total = query.Count();

        // 4. Sort.
        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Code.Value);

        // 5–7. Page, project, materialize once.
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new CurrencySelectOption(x.Id.Value, x.Code.Value))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<CurrencySelectOption>(items, total, request.Page, request.PageSize);
    }
}
