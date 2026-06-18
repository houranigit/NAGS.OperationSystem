using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Contracts.Features.AircraftType;
using Microsoft.EntityFrameworkCore;

namespace Core.Application.Features.AircraftType.Queries.GetPaginatedAircraftTypeSelectOptions;

/// <summary>
/// Reference pattern for Core <c>GetPaginated*SelectOptions</c> consumed by Host.Web dropdowns.
/// Pipeline mirrors <see cref="Customer.Queries.GetPaginatedCustomerSelectOptions.GetPaginatedCustomerSelectOptionsQueryHandler"/>:
/// IQueryable → filter → count → order → paginate → project → single <c>ToListAsync</c>.
/// </summary>
public sealed class GetPaginatedAircraftTypeSelectOptionsQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedAircraftTypeSelectOptionsQuery, PaginatedResult<AircraftTypeSelectOption>>
{
    public async Task<Result<PaginatedResult<AircraftTypeSelectOption>>> Handle(
        GetPaginatedAircraftTypeSelectOptionsQuery request,
        CancellationToken cancellationToken)
    {
        // 1. Baseline query — active rows only; stay on IQueryable until the final ToListAsync.
        var query = db.AircraftTypes.Where(x => x.IsActive).AsQueryable();

        // 2. Dynamic grid-style filters (entity property names).
        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        // 3. Total before paging.
        var total = query.Count();

        // 4. Sort — explicit OrderByQuery from caller or default (aligned with grid default).
        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Manufacturer).ThenBy(x => x.Model);

        // 5–7. Page in the database, map to Contracts select row, then materialize once.
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new AircraftTypeSelectOption(x.Id.Value, x.Model))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<AircraftTypeSelectOption>(items, total, request.Page, request.PageSize);
    }
}
