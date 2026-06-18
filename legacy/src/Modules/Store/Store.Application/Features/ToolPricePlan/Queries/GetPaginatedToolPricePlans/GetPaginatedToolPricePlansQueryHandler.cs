using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Features.Currency;
using Core.Contracts.Readers;
using Microsoft.EntityFrameworkCore;
using Store.Application.Abstractions;
using Store.Contracts.Features.Tool;
using Store.Contracts.Features.ToolPricePlan;
using StoreToolPricePlan = Store.Domain.Aggregates.ToolPricePlan.ToolPricePlan;

namespace Store.Application.Features.ToolPricePlan.Queries.GetPaginatedToolPricePlans;

public sealed class GetPaginatedToolPricePlansQueryHandler(
    IStoreDbContext db,
    ICurrencyReader currencies)
    : IQueryHandler<GetPaginatedToolPricePlansQuery, PaginatedResult<ToolPricePlanDto>>
{
    public async Task<Result<PaginatedResult<ToolPricePlanDto>>> Handle(
        GetPaginatedToolPricePlansQuery request,
        CancellationToken cancellationToken)
    {
        IQueryable<StoreToolPricePlan> baseQuery = db.ToolPricePlans
            .Include(p => p.Brackets)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            baseQuery = baseQuery.Where(request.FilterQuery);

        var total = baseQuery.Count();

        var ordered = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? baseQuery.OrderBy(request.OrderByQuery)
            : (
                from p in baseQuery
                join t in db.Tools on p.ToolId equals t.Id
                orderby t.Name, p.CreatedAt descending
                select p);

        var skip = (request.Page - 1) * request.PageSize;

        var paged = await (
                from p in ordered.Skip(skip).Take(request.PageSize)
                join t in db.Tools on p.ToolId equals t.Id
                select new
                {
                    Plan = p,
                    Tool = t
                })
            .ToListAsync(cancellationToken);

        var currencyIds = paged.Select(x => x.Plan.CurrencyId).Distinct().ToList();
        var snapshotTasks = currencyIds.Select(id => currencies.GetByIdAsync(id, cancellationToken));
        var snapshots = await Task.WhenAll(snapshotTasks);
        var currencyLookup = snapshots
            .Where(s => s is not null)
            .Cast<CurrencySnapshot>()
            .ToDictionary(c => c.CurrencyId, c => c);

        var items = paged
            .Select(row => new ToolPricePlanDto(
                row.Plan.Id.Value,
                new ToolSnapshot(row.Tool.Id.Value, row.Tool.Name),
                currencyLookup.TryGetValue(row.Plan.CurrencyId, out var snap)
                    ? snap
                    : new CurrencySnapshot(row.Plan.CurrencyId, string.Empty),
                row.Plan.Basis,
                row.Plan.IsActive,
                row.Plan.Brackets
                    .Select(b => new ToolPricePlanBracketDto(
                        b.MinMinutes, b.MaxMinutes, b.BlockSize, b.Value, b.BillingMode))
                    .ToList(),
                row.Plan.CreatedAt,
                row.Plan.UpdatedAt))
            .ToList();

        return new PaginatedResult<ToolPricePlanDto>(items, total, request.Page, request.PageSize);
    }
}
