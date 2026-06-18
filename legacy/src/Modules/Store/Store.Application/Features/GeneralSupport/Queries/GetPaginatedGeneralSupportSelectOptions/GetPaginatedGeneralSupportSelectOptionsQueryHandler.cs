using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Store.Application.Abstractions;
using Store.Contracts.Features.GeneralSupport;

namespace Store.Application.Features.GeneralSupport.Queries.GetPaginatedGeneralSupportSelectOptions;

public sealed class GetPaginatedGeneralSupportSelectOptionsQueryHandler(IStoreDbContext db)
    : IQueryHandler<GetPaginatedGeneralSupportSelectOptionsQuery, PaginatedResult<GeneralSupportSelectOption>>
{
    public async Task<Result<PaginatedResult<GeneralSupportSelectOption>>> Handle(
        GetPaginatedGeneralSupportSelectOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.GeneralSupports.Where(x => x.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Name);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new GeneralSupportSelectOption(x.Id.Value, x.Name))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<GeneralSupportSelectOption>(items, total, request.Page, request.PageSize);
    }
}
