using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Contracts.Features.Country;
using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;

namespace Core.Application.Features.Country.Queries.GetPaginatedCountries;

/// <summary>
/// Paginated **grid** query for countries — same pipeline as
/// <see cref="Customer.Queries.GetPaginatedCustomers.GetPaginatedCustomersQueryHandler"/>.
/// </summary>
/// <remarks>
/// Stay on <see cref="IQueryable{T}"/> until a single terminal <c>ToListAsync</c>.
/// Do not materialize the full <c>Countries</c> set before <c>Skip</c>/<c>Take</c>.
/// </remarks>
public sealed class GetPaginatedCountriesQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedCountriesQuery, PaginatedResult<CountryDto>>
{
    public async Task<Result<PaginatedResult<CountryDto>>> Handle(
        GetPaginatedCountriesQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Countries.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(c => c.Code.Value);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(c => new CountryDto(
                c.Id.Value,
                c.Code.Value,
                c.Name,
                c.IsActive,
                c.CreatedAt,
                null))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<CountryDto>(items, total, request.Page, request.PageSize);
    }
}
