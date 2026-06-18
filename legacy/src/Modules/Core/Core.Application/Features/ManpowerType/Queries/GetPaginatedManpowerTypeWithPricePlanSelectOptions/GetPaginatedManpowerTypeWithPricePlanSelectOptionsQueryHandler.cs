using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Application.Features.Customer.Queries.GetPaginatedCustomerSelectOptions;
using Core.Contracts.Features.ManpowerType;
using Core.Contracts.Features.Pricing;
using Microsoft.EntityFrameworkCore;

namespace Core.Application.Features.ManpowerType.Queries.GetPaginatedManpowerTypeWithPricePlanSelectOptions;

/// <summary>
/// Pipeline mirrors <see cref="GetPaginatedCustomerSelectOptionsQueryHandler"/>; the per-row
/// plan lookup is a correlated subquery sized to the page (active plans only). Each manpower
/// price plan is keyed only by OperationType (no aircraft dimension).
/// </summary>
public sealed class GetPaginatedManpowerTypeWithPricePlanSelectOptionsQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedManpowerTypeWithPricePlanSelectOptionsQuery, PaginatedResult<ManpowerTypeWithPricePlanSelectOption>>
{
    public async Task<Result<PaginatedResult<ManpowerTypeWithPricePlanSelectOption>>> Handle(
        GetPaginatedManpowerTypeWithPricePlanSelectOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.ManpowerTypes.Where(x => x.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Name);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(m => new ManpowerTypeWithPricePlanSelectOption(
                m.Id.Value,
                m.Name,
                db.ManpowerPricePlans
                    .Where(p => p.IsActive && p.ManpowerTypeId == m.Id)
                    .Select(p => new PricePlanScopeOption(
                        p.Id.Value,
                        p.OperationTypeId.Value,
                        (Guid?)null,
                        p.CurrencyId.Value,
                        p.Basis,
                        p.Brackets.Select(b => new PriceBracketDto(
                            b.MinMinutes,
                            b.MaxMinutes,
                            b.BlockSize,
                            b.Value,
                            b.BillingMode)).ToList()))
                    .ToList()))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<ManpowerTypeWithPricePlanSelectOption>(items, total, request.Page, request.PageSize);
    }
}
