using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Contracts.Features.Customer;
using Microsoft.EntityFrameworkCore;

namespace Core.Application.Features.Customer.Queries.GetPaginatedCustomerSelectOptions;

/// <summary>
/// Reference handler for all Core <c>GetPaginated*SelectOptions</c> queries consumed by Host.Web dropdowns.
/// Pipeline mirrors <see cref="Customer.Queries.GetPaginatedCustomers.GetPaginatedCustomersQueryHandler"/>:
/// IQueryable → filter → count → order → paginate → project → single ToListAsync (no full-table materialize).
/// </summary>
public sealed class GetPaginatedCustomerSelectOptionsQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedCustomerSelectOptionsQuery, PaginatedResult<CustomerSelectOption>>
{
    public async Task<Result<PaginatedResult<CustomerSelectOption>>> Handle(
        GetPaginatedCustomerSelectOptionsQuery request,
        CancellationToken cancellationToken)
    {
        // 1. Baseline query — stay on IQueryable until the final ToListAsync.
        var query = db.Customers.Where(x => x.IsActive).AsQueryable();

        // 2. Dynamic grid-style filters (entity property names).
        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        // 3. Total before paging.
        var total = query.Count();

        // 4. Sort — explicit OrderByQuery from caller or default.
        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Name);

        // 5–7. Page in the database, map to Contracts select row, then materialize once.
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new CustomerSelectOption(x.Id.Value, x.IataCode.Value, x.Name))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<CustomerSelectOption>(items, total, request.Page, request.PageSize);
    }
}
