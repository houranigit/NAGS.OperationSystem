using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Store.Application.Abstractions;
using Store.Contracts.Features.Material;
using Store.Contracts.Features.Pricing;

namespace Store.Application.Features.Material.Queries.GetPaginatedMaterialWithPricePlanSelectOptions;

/// <summary>
/// Pipeline mirrors <see cref="GetPaginatedMaterialSelectOptions.GetPaginatedMaterialSelectOptionsQueryHandler"/>;
/// per-row plan lookup is a correlated subquery over active plans, sized to the page.
/// </summary>
public sealed class GetPaginatedMaterialWithPricePlanSelectOptionsQueryHandler(IStoreDbContext db)
    : IQueryHandler<GetPaginatedMaterialWithPricePlanSelectOptionsQuery, PaginatedResult<MaterialWithPricePlanSelectOption>>
{
    public async Task<Result<PaginatedResult<MaterialWithPricePlanSelectOption>>> Handle(
        GetPaginatedMaterialWithPricePlanSelectOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Materials.Where(x => x.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Name);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(m => new MaterialWithPricePlanSelectOption(
                m.Id.Value,
                m.Name,
                db.MaterialPricePlans
                    .Where(p => p.IsActive && p.MaterialId == m.Id)
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

        return new PaginatedResult<MaterialWithPricePlanSelectOption>(items, total, request.Page, request.PageSize);
    }
}
