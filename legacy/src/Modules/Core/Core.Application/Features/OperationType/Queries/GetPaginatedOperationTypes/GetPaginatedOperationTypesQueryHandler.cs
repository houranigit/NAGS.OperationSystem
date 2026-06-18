using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Application.Features.Customer.Queries.GetPaginatedCustomers;
using Core.Contracts.Features.OperationType;
using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;

namespace Core.Application.Features.OperationType.Queries.GetPaginatedOperationTypes;

/// <summary>
/// Paginated grid for operation types. Same pipeline as <see cref="GetPaginatedCustomersQueryHandler"/>.
/// </summary>
/// <remarks>
/// <para><b>Wrong:</b> <c>ToListAsync()</c> on the full <c>OperationTypes</c> set, then <c>AsQueryable()</c> on materialized DTOs and paging in memory.</para>
/// <para><b>Right:</b> filter → count → order → <c>Skip</c>/<c>Take</c> → <c>Select</c> to <see cref="OperationTypeDto"/> → one <c>ToListAsync</c>.</para>
/// </remarks>
public sealed class GetPaginatedOperationTypesQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedOperationTypesQuery, PaginatedResult<OperationTypeDto>>
{
    public async Task<Result<PaginatedResult<OperationTypeDto>>> Handle(
        GetPaginatedOperationTypesQuery request,
        CancellationToken cancellationToken)
    {
        // 1. Root query — stay on IQueryable until one final ToListAsync.
        var query = db.OperationTypes.AsQueryable();

        // 2. Dynamic filters (entity property names; align with grid column Property).
        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        // 3. Total before paging.
        var total = query.Count();

        // 4. Sort — caller OrderByQuery or default.
        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Name);

        // 5–7. Page, project to DTO, single ToListAsync.
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new OperationTypeDto(
                x.Id.Value,
                x.Name,
                x.Description,
                x.IsActive,
                x.CreatedAt,
                x.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<OperationTypeDto>(items, total, request.Page, request.PageSize);
    }
}
