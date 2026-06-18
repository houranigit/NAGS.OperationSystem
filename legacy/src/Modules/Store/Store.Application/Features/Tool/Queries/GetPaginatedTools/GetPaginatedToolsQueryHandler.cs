using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Store.Application.Abstractions;
using Store.Contracts.Features.Tool;

namespace Store.Application.Features.Tool.Queries.GetPaginatedTools;

public sealed class GetPaginatedToolsQueryHandler(IStoreDbContext db)
    : IQueryHandler<GetPaginatedToolsQuery, PaginatedResult<ToolDto>>
{
    public async Task<Result<PaginatedResult<ToolDto>>> Handle(
        GetPaginatedToolsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Tools.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Name);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new ToolDto(
                x.Id.Value,
                x.Name,
                x.Description,
                x.IsActive,
                x.Equipments.Count,
                x.CreatedAt,
                x.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<ToolDto>(items, total, request.Page, request.PageSize);
    }
}
