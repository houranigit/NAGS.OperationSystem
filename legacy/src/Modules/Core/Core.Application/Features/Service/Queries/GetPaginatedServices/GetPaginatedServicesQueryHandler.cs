using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Application.Features.Customer.Queries.GetPaginatedCustomers;
using Core.Contracts.Features.Service;
using Microsoft.EntityFrameworkCore;

namespace Core.Application.Features.Service.Queries.GetPaginatedServices;

/// <summary>
/// Paginated grid for services — same pipeline as <see cref="GetPaginatedCustomersQueryHandler"/>.
/// </summary>
/// <remarks>
/// Stay on <see cref="IQueryable{T}"/> through filter → count → order → page → projection; one <c>ToListAsync</c> at the end.
/// </remarks>
public sealed class GetPaginatedServicesQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedServicesQuery, PaginatedResult<ServiceDto>>
{
    public async Task<Result<PaginatedResult<ServiceDto>>> Handle(
        GetPaginatedServicesQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Services.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Name);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(s => new ServiceDto(
                s.Id.Value,
                s.Name,
                s.Description,
                s.IsActive,
                s.CreatedAt,
                s.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<ServiceDto>(items, total, request.Page, request.PageSize);
    }
}
