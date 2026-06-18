using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Store.Application.Abstractions;
using Store.Contracts.Features.Pricing;
using Store.Contracts.Features.Tool;

namespace Store.Application.Features.Tool.Queries.GetPaginatedToolWithPricePlanSelectOptions;

/// <summary>
/// Pipeline mirrors <see cref="GetPaginatedToolSelectOptions.GetPaginatedToolSelectOptionsQueryHandler"/>;
/// the per-row plan lookup is a correlated subquery over active plans, sized to the page.
/// Store plans are not OT-scoped so <see cref="PricePlanScopeOption.OperationTypeId"/> is fixed
/// to <see cref="Guid.Empty"/> and aircraft is always <c>null</c>.
/// </summary>
public sealed class GetPaginatedToolWithPricePlanSelectOptionsQueryHandler(IStoreDbContext db)
    : IQueryHandler<GetPaginatedToolWithPricePlanSelectOptionsQuery, PaginatedResult<ToolWithPricePlanSelectOption>>
{
    public async Task<Result<PaginatedResult<ToolWithPricePlanSelectOption>>> Handle(
        GetPaginatedToolWithPricePlanSelectOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Tools.Where(x => x.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Name);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(t => new ToolWithPricePlanSelectOption(
                t.Id.Value,
                t.Name,
                db.ToolPricePlans
                    .Where(p => p.IsActive && p.ToolId == t.Id)
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

        return new PaginatedResult<ToolWithPricePlanSelectOption>(items, total, request.Page, request.PageSize);
    }
}
