using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Application.Features.Customer.Queries.GetPaginatedCustomerSelectOptions;
using Core.Application.Features.Customer.Queries.GetPaginatedCustomers;
using Core.Contracts.Features.License;
using Microsoft.EntityFrameworkCore;

namespace Core.Application.Features.License.Queries.GetPaginatedLicenseSelectOptions;

/// <summary>
/// Dropdown / lookup paging for licenses. Mirrors <see cref="GetPaginatedCustomerSelectOptionsQueryHandler"/> (<see cref="GetPaginatedCustomersQueryHandler"/> pipeline).
/// </summary>
public sealed class GetPaginatedLicenseSelectOptionsQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedLicenseSelectOptionsQuery, PaginatedResult<LicenseSelectOption>>
{
    public async Task<Result<PaginatedResult<LicenseSelectOption>>> Handle(
        GetPaginatedLicenseSelectOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Licenses.Where(x => x.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Code);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new LicenseSelectOption(x.Id.Value, x.Code, x.Name))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<LicenseSelectOption>(items, total, request.Page, request.PageSize);
    }
}
