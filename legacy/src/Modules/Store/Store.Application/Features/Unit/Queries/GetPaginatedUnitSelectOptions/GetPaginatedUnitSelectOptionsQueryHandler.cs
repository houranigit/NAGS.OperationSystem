using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Store.Application.Abstractions;
using Store.Contracts.Features.Unit;

namespace Store.Application.Features.Unit.Queries.GetPaginatedUnitSelectOptions;

public sealed class GetPaginatedUnitSelectOptionsQueryHandler(IStoreDbContext db)
    : IQueryHandler<GetPaginatedUnitSelectOptionsQuery, PaginatedResult<UnitSelectOption>>
{
    public async Task<Result<PaginatedResult<UnitSelectOption>>> Handle(
        GetPaginatedUnitSelectOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Units.Where(x => x.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Code);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new UnitSelectOption(x.Id.Value, x.Code, x.Name))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<UnitSelectOption>(items, total, request.Page, request.PageSize);
    }
}
