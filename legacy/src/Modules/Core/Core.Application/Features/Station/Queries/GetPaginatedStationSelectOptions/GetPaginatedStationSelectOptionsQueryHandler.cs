using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Contracts.Features.Station;
using Microsoft.EntityFrameworkCore;

namespace Core.Application.Features.Station.Queries.GetPaginatedStationSelectOptions;

public sealed class GetPaginatedStationSelectOptionsQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedStationSelectOptionsQuery, PaginatedResult<StationSelectOption>>
{
    public async Task<Result<PaginatedResult<StationSelectOption>>> Handle(
        GetPaginatedStationSelectOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Stations.Where(x => x.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Name);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new StationSelectOption(x.Id.Value, x.Name, x.IataCode.Value))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<StationSelectOption>(items, total, request.Page, request.PageSize);
    }
}
