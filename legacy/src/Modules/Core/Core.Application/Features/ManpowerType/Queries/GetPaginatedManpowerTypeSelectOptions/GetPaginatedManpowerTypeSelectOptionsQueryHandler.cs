using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Application.Features.Customer.Queries.GetPaginatedCustomerSelectOptions;
using Core.Contracts.Features.ManpowerType;
using Microsoft.EntityFrameworkCore;

namespace Core.Application.Features.ManpowerType.Queries.GetPaginatedManpowerTypeSelectOptions;

/// <summary>
/// Paginated select-options for Host.Web dropdowns — mirrors <see cref="GetPaginatedCustomerSelectOptionsQueryHandler"/>.
/// </summary>
/// <remarks>
/// <see cref="IQueryable{T}"/> → filter → count → order → page → project → one <c>ToListAsync</c>.
/// </remarks>
public sealed class GetPaginatedManpowerTypeSelectOptionsQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedManpowerTypeSelectOptionsQuery, PaginatedResult<ManpowerTypeSelectOption>>
{
    public async Task<Result<PaginatedResult<ManpowerTypeSelectOption>>> Handle(
        GetPaginatedManpowerTypeSelectOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.ManpowerTypes.Where(x => x.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Name);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new ManpowerTypeSelectOption(x.Id.Value, x.Name))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<ManpowerTypeSelectOption>(items, total, request.Page, request.PageSize);
    }
}
