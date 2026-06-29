using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using MasterData.Application.Abstractions;
using MasterData.Application.Contracts;
using MasterData.Domain.Materials;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.Materials;

public sealed record GetMaterialsQuery(int Page = 1, int PageSize = 20, string? Search = null, bool? IsActive = null, string? Sort = null)
    : IQuery<PagedResult<MaterialListItemDto>>;

public sealed class GetMaterialsQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetMaterialsQuery, PagedResult<MaterialListItemDto>>
{
    public async Task<Result<PagedResult<MaterialListItemDto>>> Handle(GetMaterialsQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var query = db.Materials.AsNoTracking();

        if (request.IsActive is { } active)
            query = query.Where(m => m.IsActive == active);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(m => m.Name.Contains(term) || (m.Description != null && m.Description.Contains(term)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await ApplySort(query, request.Sort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MaterialListItemDto(m.Id, m.Name, m.Description, m.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResult<MaterialListItemDto>(items, page, pageSize, total);
    }

    private static IQueryable<Material> ApplySort(IQueryable<Material> query, string? sort)
    {
        if (SortSpec.Parse(sort) is not { } spec)
            return query.OrderBy(m => m.Name);

        return spec.Field switch
        {
            "name" => spec.Descending ? query.OrderByDescending(m => m.Name) : query.OrderBy(m => m.Name),
            "isactive" => spec.Descending ? query.OrderByDescending(m => m.IsActive) : query.OrderBy(m => m.IsActive),
            _ => query.OrderBy(m => m.Name)
        };
    }
}

public sealed record GetMaterialByIdQuery(Guid Id) : IQuery<MaterialDto>;

public sealed class GetMaterialByIdQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetMaterialByIdQuery, MaterialDto>
{
    public async Task<Result<MaterialDto>> Handle(GetMaterialByIdQuery request, CancellationToken cancellationToken)
    {
        var material = await db.Materials.AsNoTracking().FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken);
        if (material is null)
            return Error.NotFound("Material not found.", "MasterData.Material.NotFound");

        return new MaterialDto(
            material.Id, material.Name, material.Description, material.IsActive,
            material.CreatedAtUtc, material.UpdatedAtUtc, Convert.ToBase64String(material.RowVersion));
    }
}

public sealed record GetActiveMaterialOptionsQuery : IQuery<IReadOnlyList<MaterialOptionDto>>;

public sealed class GetActiveMaterialOptionsQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetActiveMaterialOptionsQuery, IReadOnlyList<MaterialOptionDto>>
{
    public async Task<Result<IReadOnlyList<MaterialOptionDto>>> Handle(GetActiveMaterialOptionsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<MaterialOptionDto> options = await db.Materials.AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.Name)
            .Select(m => new MaterialOptionDto(m.Id, m.Name))
            .ToListAsync(cancellationToken);

        return Result.Success(options);
    }
}
