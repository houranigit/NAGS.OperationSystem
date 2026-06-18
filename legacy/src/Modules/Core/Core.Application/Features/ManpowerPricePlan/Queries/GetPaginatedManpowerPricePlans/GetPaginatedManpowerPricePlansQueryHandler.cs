using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Contracts.Features.Currency;
using Core.Contracts.Features.ManpowerPricePlan;
using Core.Contracts.Features.ManpowerType;
using Core.Contracts.Features.OperationType;
using Microsoft.EntityFrameworkCore;
using CoreManpowerPlan = Core.Domain.Aggregates.ManpowerPricePlan.ManpowerPricePlan;

namespace Core.Application.Features.ManpowerPricePlan.Queries.GetPaginatedManpowerPricePlans;

/// <summary>
/// Paginated manpower price plans grid query — stays on <see cref="IQueryable{T}"/> until one final materialization (same discipline as Customers).
/// </summary>
/// <remarks>
/// <para><b>Wrong:</b> materializing entire <c>ManpowerPricePlans</c>/<c>ManpowerTypes</c>/<c>Currencies</c> sets with <c>ToListAsync</c>, paging in memory, or wrapping with <c>AsQueryable()</c> on CLR lists.</para>
/// <para><b>Right:</b> filter → count → order → page on the DB → project to DTO with lookups — see <see cref="Core.Application.Features.Customer.Queries.GetPaginatedCustomers.GetPaginatedCustomersQueryHandler"/>.</para>
/// </remarks>
public sealed class GetPaginatedManpowerPricePlansQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedManpowerPricePlansQuery, PaginatedResult<ManpowerPricePlanDto>>
{
    public async Task<Result<PaginatedResult<ManpowerPricePlanDto>>> Handle(
        GetPaginatedManpowerPricePlansQuery request,
        CancellationToken cancellationToken)
    {
        // 1–2. Root + bracket-owned rows loaded for projection — filter on aggregate properties (matches grid filter names).
        IQueryable<CoreManpowerPlan> baseQuery = db.ManpowerPricePlans
            .Include(p => p.Brackets)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            baseQuery = baseQuery.Where(request.FilterQuery);

        // 3. Total before paging — still IQueryable.
        var total = baseQuery.Count();

        // 4. Sort — caller OrderByQuery, or deterministic default by manpower then operation labels.
        var orderedPlans = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? baseQuery.OrderBy(request.OrderByQuery)
            : (
                from p in baseQuery
                join mt in db.ManpowerTypes on p.ManpowerTypeId equals mt.Id
                join ot in db.OperationTypes on p.OperationTypeId equals ot.Id
                orderby mt.Name, ot.Name, p.CreatedAt descending
                select p);

        var skip = (request.Page - 1) * request.PageSize;

        // 5–7. Page on the DB, join lookup labels, bracket snapshot in Select — single terminal ToListAsync.
        var items = await (
                from p in orderedPlans.Skip(skip).Take(request.PageSize)
                join mt in db.ManpowerTypes on p.ManpowerTypeId equals mt.Id
                join ot in db.OperationTypes on p.OperationTypeId equals ot.Id
                join c in db.Currencies on p.CurrencyId equals c.Id
                select new ManpowerPricePlanDto(
                    p.Id.Value,
                    new ManpowerTypeSnapshot(p.ManpowerTypeId.Value, mt.Name),
                    new OperationTypeSnapshot(p.OperationTypeId.Value, ot.Name),
                    new CurrencySnapshot(p.CurrencyId.Value, c.Code.Value),
                    p.Basis,
                    p.Brackets.Select(b =>
                            new ManpowerPricePlanBracketDto(
                                b.MinMinutes,
                                b.MaxMinutes,
                                b.BlockSize,
                                b.Value,
                                b.BillingMode))
                        .ToList(),
                    p.CreatedAt,
                    p.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<ManpowerPricePlanDto>(items, total, request.Page, request.PageSize);
    }
}
