using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Store.Application.Abstractions;
using Store.Contracts.Features.Unit;

namespace Store.Application.Features.Unit.Queries.GetPaginatedUnits;

/// <summary>Paginated grid query for units — IQueryable → filter → count → order → page → project.</summary>
public sealed class GetPaginatedUnitsQueryHandler(IStoreDbContext db)
    : IQueryHandler<GetPaginatedUnitsQuery, PaginatedResult<UnitDto>>
{
    public async Task<Result<PaginatedResult<UnitDto>>> Handle(
        GetPaginatedUnitsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Units.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Code);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new UnitDto(
                x.Id.Value,
                x.Code,
                x.Name,
                x.IsActive,
                x.CreatedAt,
                x.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<UnitDto>(items, total, request.Page, request.PageSize);
    }
}
