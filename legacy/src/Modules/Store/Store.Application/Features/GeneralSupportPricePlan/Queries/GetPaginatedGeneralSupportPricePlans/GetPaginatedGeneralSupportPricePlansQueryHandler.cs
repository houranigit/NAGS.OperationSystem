using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Features.Currency;
using Core.Contracts.Readers;
using Microsoft.EntityFrameworkCore;
using Store.Application.Abstractions;
using Store.Contracts.Features.GeneralSupport;
using Store.Contracts.Features.GeneralSupportPricePlan;
using StoreGeneralSupportPricePlan = Store.Domain.Aggregates.GeneralSupportPricePlan.GeneralSupportPricePlan;

namespace Store.Application.Features.GeneralSupportPricePlan.Queries.GetPaginatedGeneralSupportPricePlans;

public sealed class GetPaginatedGeneralSupportPricePlansQueryHandler(
    IStoreDbContext db,
    ICurrencyReader currencies)
    : IQueryHandler<GetPaginatedGeneralSupportPricePlansQuery, PaginatedResult<GeneralSupportPricePlanDto>>
{
    public async Task<Result<PaginatedResult<GeneralSupportPricePlanDto>>> Handle(
        GetPaginatedGeneralSupportPricePlansQuery request,
        CancellationToken cancellationToken)
    {
        IQueryable<StoreGeneralSupportPricePlan> baseQuery = db.GeneralSupportPricePlans
            .Include(p => p.Brackets)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            baseQuery = baseQuery.Where(request.FilterQuery);

        var total = baseQuery.Count();

        var ordered = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? baseQuery.OrderBy(request.OrderByQuery)
            : (
                from p in baseQuery
                join g in db.GeneralSupports on p.GeneralSupportId equals g.Id
                orderby g.Name, p.CreatedAt descending
                select p);

        var skip = (request.Page - 1) * request.PageSize;

        var paged = await (
                from p in ordered.Skip(skip).Take(request.PageSize)
                join g in db.GeneralSupports on p.GeneralSupportId equals g.Id
                select new
                {
                    Plan = p,
                    GeneralSupport = g
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
            .Select(row => new GeneralSupportPricePlanDto(
                row.Plan.Id.Value,
                new GeneralSupportSnapshot(row.GeneralSupport.Id.Value, row.GeneralSupport.Name),
                currencyLookup.TryGetValue(row.Plan.CurrencyId, out var snap)
                    ? snap
                    : new CurrencySnapshot(row.Plan.CurrencyId, string.Empty),
                row.Plan.Basis,
                row.Plan.IsActive,
                row.Plan.Brackets
                    .Select(b => new GeneralSupportPricePlanBracketDto(
                        b.MinMinutes, b.MaxMinutes, b.BlockSize, b.Value, b.BillingMode))
                    .ToList(),
                row.Plan.CreatedAt,
                row.Plan.UpdatedAt))
            .ToList();

        return new PaginatedResult<GeneralSupportPricePlanDto>(items, total, request.Page, request.PageSize);
    }
}
