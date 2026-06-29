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
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var query = db.Tools.AsNoTracking();

        if (request.IsActive is { } active)
            query = query.Where(t => t.IsActive == active);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(t => t.Name.Contains(term) || (t.Description != null && t.Description.Contains(term)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await ApplySort(query, request.Sort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new ToolListItemDto(t.Id, t.Name, t.Description, t.IsActive, t.Equipments.Count))
            .ToListAsync(cancellationToken);

        return new PagedResult<ToolListItemDto>(items, page, pageSize, total);
    }

    private static IQueryable<Tool> ApplySort(IQueryable<Tool> query, string? sort)
    {
        if (SortSpec.Parse(sort) is not { } spec)
            return query.OrderBy(t => t.Name);

        return spec.Field switch
        {
            "name" => spec.Descending ? query.OrderByDescending(t => t.Name) : query.OrderBy(t => t.Name),
            "isactive" => spec.Descending ? query.OrderByDescending(t => t.IsActive) : query.OrderBy(t => t.IsActive),
            _ => query.OrderBy(t => t.Name)
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
            .Select(t => new ToolOptionDto(t.Id, t.Name))
            .ToListAsync(cancellationToken);

        return Result.Success(options);
    }
}
