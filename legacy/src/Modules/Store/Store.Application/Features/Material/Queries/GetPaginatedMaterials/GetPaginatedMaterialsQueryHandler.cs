using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Store.Application.Abstractions;
using Store.Contracts.Features.Material;
using Store.Contracts.Features.Unit;

namespace Store.Application.Features.Material.Queries.GetPaginatedMaterials;

public sealed class GetPaginatedMaterialsQueryHandler(IStoreDbContext db)
    : IQueryHandler<GetPaginatedMaterialsQuery, PaginatedResult<MaterialDto>>
{
    public async Task<Result<PaginatedResult<MaterialDto>>> Handle(
        GetPaginatedMaterialsQuery request,
        CancellationToken cancellationToken)
    {
        var baseQuery = db.Materials.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            baseQuery = baseQuery.Where(request.FilterQuery);

        var total = baseQuery.Count();

        var ordered = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? baseQuery.OrderBy(request.OrderByQuery)
            : baseQuery.OrderBy(x => x.Name);

        var skip = (request.Page - 1) * request.PageSize;

        var items = await (
                from m in ordered.Skip(skip).Take(request.PageSize)
                join u in db.Units on m.UnitId equals u.Id
                select new MaterialDto(
                    m.Id.Value,
                    m.Name,
                    new UnitSnapshot(u.Id.Value, u.Code, u.Name),
                    m.IsActive,
                    m.CreatedAt,
                    m.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<MaterialDto>(items, total, request.Page, request.PageSize);
    }
}
