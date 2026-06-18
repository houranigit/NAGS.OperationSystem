using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Store.Application.Abstractions;
using Store.Contracts.Features.Tool;

namespace Store.Application.Features.Tool.Queries.GetPaginatedToolSelectOptions;

public sealed class GetPaginatedToolSelectOptionsQueryHandler(IStoreDbContext db)
    : IQueryHandler<GetPaginatedToolSelectOptionsQuery, PaginatedResult<ToolSelectOption>>
{
    public async Task<Result<PaginatedResult<ToolSelectOption>>> Handle(
        GetPaginatedToolSelectOptionsQuery request,
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
            .Select(x => new ToolSelectOption(x.Id.Value, x.Name))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<ToolSelectOption>(items, total, request.Page, request.PageSize);
    }
}
