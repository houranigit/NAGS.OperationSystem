using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Features.Currency;
using Core.Contracts.Readers;
using Microsoft.EntityFrameworkCore;
using Store.Application.Abstractions;
using Store.Contracts.Features.Material;
using Store.Contracts.Features.MaterialPricePlan;
using StoreMaterialPricePlan = Store.Domain.Aggregates.MaterialPricePlan.MaterialPricePlan;

namespace Store.Application.Features.MaterialPricePlan.Queries.GetPaginatedMaterialPricePlans;

public sealed class GetPaginatedMaterialPricePlansQueryHandler(
    IStoreDbContext db,
    ICurrencyReader currencies)
    : IQueryHandler<GetPaginatedMaterialPricePlansQuery, PaginatedResult<MaterialPricePlanDto>>
{
    public async Task<Result<PaginatedResult<MaterialPricePlanDto>>> Handle(
        GetPaginatedMaterialPricePlansQuery request,
        CancellationToken cancellationToken)
    {
        IQueryable<StoreMaterialPricePlan> baseQuery = db.MaterialPricePlans
            .Include(p => p.Brackets)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            baseQuery = baseQuery.Where(request.FilterQuery);

        var total = baseQuery.Count();

        var ordered = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? baseQuery.OrderBy(request.OrderByQuery)
            : (
                from p in baseQuery
                join m in db.Materials on p.MaterialId equals m.Id
                orderby m.Name, p.CreatedAt descending
                select p);

        var skip = (request.Page - 1) * request.PageSize;

        var paged = await (
                from p in ordered.Skip(skip).Take(request.PageSize)
                join m in db.Materials on p.MaterialId equals m.Id
                select new
                {
                    Plan = p,
                    Material = m
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
            .Select(row => new MaterialPricePlanDto(
                row.Plan.Id.Value,
                new MaterialSnapshot(row.Material.Id.Value, row.Material.Name),
                currencyLookup.TryGetValue(row.Plan.CurrencyId, out var snap)
                    ? snap
                    : new CurrencySnapshot(row.Plan.CurrencyId, string.Empty),
                row.Plan.Basis,
                row.Plan.IsActive,
                row.Plan.Brackets
                    .Select(b => new MaterialPricePlanBracketDto(
                        b.MinMinutes, b.MaxMinutes, b.BlockSize, b.Value, b.BillingMode))
                    .ToList(),
                row.Plan.CreatedAt,
                row.Plan.UpdatedAt))
            .ToList();

        return new PaginatedResult<MaterialPricePlanDto>(items, total, request.Page, request.PageSize);
    }
}
