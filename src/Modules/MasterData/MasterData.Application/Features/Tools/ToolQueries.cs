using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using MasterData.Application.Abstractions;
using MasterData.Application.Contracts;
using MasterData.Domain.Tools;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.Tools;

public sealed record GetToolsQuery(int Page = 1, int PageSize = 20, string? Search = null, bool? IsActive = null, string? Sort = null)
    : IQuery<PagedResult<ToolListItemDto>>;

public sealed class GetToolsQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetToolsQuery, PagedResult<ToolListItemDto>>
{
    public async Task<Result<PagedResult<ToolListItemDto>>> Handle(GetToolsQuery request, CancellationToken cancellationToken)
    {
        var paging = PageRequest.From(request.Page, request.PageSize);
        var query = db.Tools.AsNoTracking();

        if (request.IsActive is { } active)
            query = query.Where(t => t.IsActive == active);

        if (SearchFilter.Term(request.Search) is { } term)
            query = query.Where(t => t.Name.ToLower().Contains(term) || (t.Description != null && t.Description.ToLower().Contains(term)));

        var total = await query.LongCountAsync(cancellationToken);
        if (paging.IsOutOfRange(total))
            return paging.Empty<ToolListItemDto>(total);

        var items = await ApplySort(query, request.Sort)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(t => new ToolListItemDto(t.Id, t.Name, t.Description, t.IsActive, t.Equipments.Count))
            .ToListAsync(cancellationToken);

        return paging.ToResult<ToolListItemDto>(items, total);
    }

    private static IQueryable<Tool> ApplySort(IQueryable<Tool> query, string? sort)
    {
        if (SortSpec.Parse(sort) is not { } spec)
            return query.OrderBy(t => t.Name).ThenBy(t => t.Id);

        return spec.Field switch
        {
            "name" => spec.Descending ? query.OrderByDescending(t => t.Name).ThenByDescending(t => t.Id) : query.OrderBy(t => t.Name).ThenBy(t => t.Id),
            "isactive" => spec.Descending ? query.OrderByDescending(t => t.IsActive).ThenByDescending(t => t.Id) : query.OrderBy(t => t.IsActive).ThenBy(t => t.Id),
            _ => query.OrderBy(t => t.Name).ThenBy(t => t.Id)
        };
    }
}

public sealed record GetToolByIdQuery(Guid Id) : IQuery<ToolDto>;

public sealed class GetToolByIdQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetToolByIdQuery, ToolDto>
{
    public async Task<Result<ToolDto>> Handle(GetToolByIdQuery request, CancellationToken cancellationToken)
    {
        var tool = await db.Tools.AsNoTracking()
            .Where(t => t.Id == request.Id)
            .Select(t => new ToolDto(
                t.Id,
                t.Name,
                t.Description,
                t.IsActive,
                t.CreatedAtUtc,
                t.UpdatedAtUtc,
                Convert.ToBase64String(t.RowVersion),
                t.Equipments
                    .OrderBy(e => e.FactoryId)
                    .ThenBy(e => e.SerialId)
                    .ThenBy(e => e.Id)
                    .Select(e => new ToolEquipmentDto(e.Id, e.FactoryId, e.SerialId, e.CalibrationDate))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);

        return tool is null
            ? Error.NotFound("Tool not found.", "MasterData.Tool.NotFound")
            : tool;
    }
}

public sealed record GetActiveToolOptionsQuery : IQuery<IReadOnlyList<ToolOptionDto>>;

public sealed class GetActiveToolOptionsQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetActiveToolOptionsQuery, IReadOnlyList<ToolOptionDto>>
{
    public async Task<Result<IReadOnlyList<ToolOptionDto>>> Handle(GetActiveToolOptionsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<ToolOptionDto> options = await db.Tools.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .ThenBy(t => t.Id)
            .Select(t => new ToolOptionDto(t.Id, t.Name))
            .ToListAsync(cancellationToken);

        return Result.Success(options);
    }
}
