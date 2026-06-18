using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Contracts.Features.AircraftType;
using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;

namespace Core.Application.Features.AircraftType.Queries.GetPaginatedAircraftTypes;

/// <summary>
/// Paginated **grid** query for aircraft types — same pipeline as
/// <see cref="Customer.Queries.GetPaginatedCustomers.GetPaginatedCustomersQueryHandler"/>.
/// </summary>
/// <remarks>
/// Stay on <see cref="IQueryable{T}"/> until a single terminal <c>ToListAsync</c>.
/// Do not call <c>ToListAsync()</c> on the full DbSet before <c>Skip</c>/<c>Take</c>.
/// </remarks>
public sealed class GetPaginatedAircraftTypesQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedAircraftTypesQuery, PaginatedResult<AircraftTypeDto>>
{
    public async Task<Result<PaginatedResult<AircraftTypeDto>>> Handle(
        GetPaginatedAircraftTypesQuery request,
        CancellationToken cancellationToken)
    {
        // 1. Root query — stay on IQueryable until the final ToListAsync.
        var query = db.AircraftTypes.AsQueryable();

        // 2. Dynamic filters — entity property names (Radzen grid filter strings).
        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        // 3. Total count before paging (executes on server; no full-table client materialize).
        var total = query.Count();

        // 4. Sort — caller OrderByQuery or default.
        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Manufacturer).ThenBy(x => x.Model);

        // 5–7. Page in the database, project to DTO, single ToListAsync.
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new AircraftTypeDto(
                x.Id.Value,
                x.Manufacturer,
                x.Model,
                x.Notes,
                x.IsActive,
                x.CreatedAt,
                x.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<AircraftTypeDto>(items, total, request.Page, request.PageSize);
    }
}
