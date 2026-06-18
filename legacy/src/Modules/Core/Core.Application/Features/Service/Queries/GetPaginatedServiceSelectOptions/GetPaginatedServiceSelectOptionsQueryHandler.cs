using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Application.Features.Customer.Queries.GetPaginatedCustomerSelectOptions;
using Core.Contracts.Features.Service;
using Microsoft.EntityFrameworkCore;

namespace Core.Application.Features.Service.Queries.GetPaginatedServiceSelectOptions;

/// <summary>
/// Paginated dropdown options — pipeline mirrors <see cref="GetPaginatedCustomerSelectOptionsQueryHandler"/>.
/// </summary>
public sealed class GetPaginatedServiceSelectOptionsQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedServiceSelectOptionsQuery, PaginatedResult<ServiceSelectOption>>
{
    public async Task<Result<PaginatedResult<ServiceSelectOption>>> Handle(
        GetPaginatedServiceSelectOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Services.Where(x => x.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Name);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new ServiceSelectOption(x.Id.Value, x.Name))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<ServiceSelectOption>(items, total, request.Page, request.PageSize);
    }
}
