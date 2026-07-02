using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using MasterData.Application.Abstractions;
using MasterData.Application.Contracts;
using MasterData.Domain.GeneralSupports;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.GeneralSupports;

public sealed record GetGeneralSupportsQuery(int Page = 1, int PageSize = 20, string? Search = null, bool? IsActive = null, string? Sort = null)
    : IQuery<PagedResult<GeneralSupportListItemDto>>;

public sealed class GetGeneralSupportsQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetGeneralSupportsQuery, PagedResult<GeneralSupportListItemDto>>
{
    public async Task<Result<PagedResult<GeneralSupportListItemDto>>> Handle(GetGeneralSupportsQuery request, CancellationToken cancellationToken)
    {
        var paging = PageRequest.From(request.Page, request.PageSize);
        var query = db.GeneralSupports.AsNoTracking();

        if (request.IsActive is { } active)
            query = query.Where(g => g.IsActive == active);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(g => g.Name.Contains(term) || (g.Description != null && g.Description.Contains(term)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        if (paging.IsOutOfRange(total))
            return paging.Empty<GeneralSupportListItemDto>(total);

        var items = await ApplySort(query, request.Sort)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(g => new GeneralSupportListItemDto(g.Id, g.Name, g.Description, g.IsActive))
            .ToListAsync(cancellationToken);

        return paging.ToResult<GeneralSupportListItemDto>(items, total);
    }

    private static IQueryable<GeneralSupport> ApplySort(IQueryable<GeneralSupport> query, string? sort)
    {
        if (SortSpec.Parse(sort) is not { } spec)
            return query.OrderBy(g => g.Name).ThenBy(g => g.Id);

        return spec.Field switch
        {
            "name" => spec.Descending ? query.OrderByDescending(g => g.Name).ThenByDescending(g => g.Id) : query.OrderBy(g => g.Name).ThenBy(g => g.Id),
            "isactive" => spec.Descending ? query.OrderByDescending(g => g.IsActive).ThenByDescending(g => g.Id) : query.OrderBy(g => g.IsActive).ThenBy(g => g.Id),
            _ => query.OrderBy(g => g.Name).ThenBy(g => g.Id)
        };
    }
}

public sealed record GetGeneralSupportByIdQuery(Guid Id) : IQuery<GeneralSupportDto>;

public sealed class GetGeneralSupportByIdQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetGeneralSupportByIdQuery, GeneralSupportDto>
{
    public async Task<Result<GeneralSupportDto>> Handle(GetGeneralSupportByIdQuery request, CancellationToken cancellationToken)
    {
        var support = await db.GeneralSupports.AsNoTracking().FirstOrDefaultAsync(g => g.Id == request.Id, cancellationToken);
        if (support is null)
            return Error.NotFound("General support item not found.", "MasterData.GeneralSupport.NotFound");

        return new GeneralSupportDto(
            support.Id, support.Name, support.Description, support.IsActive,
            support.CreatedAtUtc, support.UpdatedAtUtc, Convert.ToBase64String(support.RowVersion));
    }
}

public sealed record GetActiveGeneralSupportOptionsQuery : IQuery<IReadOnlyList<GeneralSupportOptionDto>>;

public sealed class GetActiveGeneralSupportOptionsQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetActiveGeneralSupportOptionsQuery, IReadOnlyList<GeneralSupportOptionDto>>
{
    public async Task<Result<IReadOnlyList<GeneralSupportOptionDto>>> Handle(GetActiveGeneralSupportOptionsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<GeneralSupportOptionDto> options = await db.GeneralSupports.AsNoTracking()
            .Where(g => g.IsActive)
            .OrderBy(g => g.Name)
            .ThenBy(g => g.Id)
            .Select(g => new GeneralSupportOptionDto(g.Id, g.Name))
            .ToListAsync(cancellationToken);

        return Result.Success(options);
    }
}
