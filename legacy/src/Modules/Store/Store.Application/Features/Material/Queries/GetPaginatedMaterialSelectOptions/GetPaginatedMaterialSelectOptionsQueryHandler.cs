using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Store.Application.Abstractions;
using Store.Contracts.Features.Material;

namespace Store.Application.Features.Material.Queries.GetPaginatedMaterialSelectOptions;

public sealed class GetPaginatedMaterialSelectOptionsQueryHandler(IStoreDbContext db)
    : IQueryHandler<GetPaginatedMaterialSelectOptionsQuery, PaginatedResult<MaterialSelectOption>>
{
    public async Task<Result<PaginatedResult<MaterialSelectOption>>> Handle(
        GetPaginatedMaterialSelectOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Materials.Where(x => x.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Name);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new MaterialSelectOption(x.Id.Value, x.Name))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<MaterialSelectOption>(items, total, request.Page, request.PageSize);
    }
}
