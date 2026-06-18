using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Store.Application.Abstractions;
using Store.Contracts.Features.GeneralSupport;
using Store.Contracts.Features.Pricing;

namespace Store.Application.Features.GeneralSupport.Queries.GetPaginatedGeneralSupportWithPricePlanSelectOptions;

/// <summary>
/// Pipeline mirrors <see cref="GetPaginatedGeneralSupportSelectOptions.GetPaginatedGeneralSupportSelectOptionsQueryHandler"/>;
/// per-row plan lookup is a correlated subquery over active plans, sized to the page.
/// </summary>
public sealed class GetPaginatedGeneralSupportWithPricePlanSelectOptionsQueryHandler(IStoreDbContext db)
    : IQueryHandler<GetPaginatedGeneralSupportWithPricePlanSelectOptionsQuery, PaginatedResult<GeneralSupportWithPricePlanSelectOption>>
{
    public async Task<Result<PaginatedResult<GeneralSupportWithPricePlanSelectOption>>> Handle(
        GetPaginatedGeneralSupportWithPricePlanSelectOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.GeneralSupports.Where(x => x.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Name);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(g => new GeneralSupportWithPricePlanSelectOption(
                g.Id.Value,
                g.Name,
                db.GeneralSupportPricePlans
                    .Where(p => p.IsActive && p.GeneralSupportId == g.Id)
                    .Select(p => new PricePlanScopeOption(
                        p.Id.Value,
                        Guid.Empty,
                        (Guid?)null,
                        p.CurrencyId,
                        p.Basis,
                        p.Brackets.Select(b => new PriceBracketDto(
                            b.MinMinutes,
                            b.MaxMinutes,
                            b.BlockSize,
                            b.Value,
                            b.BillingMode)).ToList()))
                    .ToList()))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<GeneralSupportWithPricePlanSelectOption>(items, total, request.Page, request.PageSize);
    }
}
