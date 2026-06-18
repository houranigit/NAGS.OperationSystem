using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Application.Features.Customer.Queries.GetPaginatedCustomers;
using Core.Contracts.Features.License;
using Microsoft.EntityFrameworkCore;

namespace Core.Application.Features.License.Queries.GetPaginatedLicenses;

/// <summary>
/// Paginated **grid** handler for licenses. Same pipeline as <see cref="GetPaginatedCustomersQueryHandler"/>.
/// </summary>
/// <remarks>
/// Stay on <see cref="IQueryable{T}"/> through filter → count → order → <c>Skip</c>/<c>Take</c> → <see cref="LicenseDto"/> projection → single <c>ToListAsync</c>.
/// <c>UpdatedAt</c> is omitted from persistence for this aggregate; the DTO exposes <c>null</c>.
/// </remarks>
public sealed class GetPaginatedLicensesQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedLicensesQuery, PaginatedResult<LicenseDto>>
{
    public async Task<Result<PaginatedResult<LicenseDto>>> Handle(
        GetPaginatedLicensesQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Licenses.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Code);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new LicenseDto(
                x.Id.Value,
                x.Code,
                x.Name,
                x.Description,
                x.IsActive,
                x.CreatedAt,
                UpdatedAt: null))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<LicenseDto>(items, total, request.Page, request.PageSize);
    }
}
