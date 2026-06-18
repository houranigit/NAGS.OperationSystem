using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Contracts.Features.Country;
using Microsoft.EntityFrameworkCore;

namespace Core.Application.Features.Country.Queries.GetPaginatedCountrySelectOptions;

public sealed class GetPaginatedCountrySelectOptionsQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedCountrySelectOptionsQuery, PaginatedResult<CountrySelectOption>>
{
    public async Task<Result<PaginatedResult<CountrySelectOption>>> Handle(
        GetPaginatedCountrySelectOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Countries.Where(x => x.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Code.Value);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new CountrySelectOption(x.Id.Value, x.Code.Value, x.Name))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<CountrySelectOption>(items, total, request.Page, request.PageSize);
    }
}
