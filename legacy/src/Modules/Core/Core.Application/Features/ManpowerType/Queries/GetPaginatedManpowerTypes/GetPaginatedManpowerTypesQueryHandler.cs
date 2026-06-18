using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Application.Features.Customer.Queries.GetPaginatedCustomers;
using Core.Contracts.Features.ManpowerType;
using Microsoft.EntityFrameworkCore;

namespace Core.Application.Features.ManpowerType.Queries.GetPaginatedManpowerTypes;

/// <summary>
/// Paginated grid query for manpower types — same pipeline as <see cref="GetPaginatedCustomersQueryHandler"/>.
/// </summary>
/// <remarks>
/// Stay on <see cref="IQueryable{T}"/> through filter, count, order, page, projection, then a single <c>ToListAsync</c>.
/// Do not materialize the full <c>ManpowerTypes</c> set before <c>Skip</c>/<c>Take</c>.
/// </remarks>
public sealed class GetPaginatedManpowerTypesQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedManpowerTypesQuery, PaginatedResult<ManpowerTypeDto>>
{
    public async Task<Result<PaginatedResult<ManpowerTypeDto>>> Handle(
        GetPaginatedManpowerTypesQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.ManpowerTypes.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Name);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new ManpowerTypeDto(
                x.Id.Value,
                x.Name,
                x.Description,
                x.IsActive,
                x.CreatedAt,
                x.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<ManpowerTypeDto>(items, total, request.Page, request.PageSize);
    }
}
