using System.Linq.Expressions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using MasterData.Application.Abstractions;
using MasterData.Application.Contracts;
using MasterData.Domain.AtaChapters;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.AtaChapters;

internal static class AtaChapterProjections
{
    public static readonly Expression<Func<AtaChapterCategory, AtaChapterCategoryDto>> Category = x =>
        new AtaChapterCategoryDto(x.Id, x.Name, x.IsActive, x.CreatedAtUtc, x.UpdatedAtUtc, Convert.ToBase64String(x.RowVersion));

    public static readonly Expression<Func<AtaChapter, AtaChapterDto>> Chapter = x =>
        new AtaChapterDto(x.Id, x.CategoryId, x.Category.Name, x.Code, x.Title, x.IsActive,
            x.Category.IsActive, x.CreatedAtUtc, x.UpdatedAtUtc, Convert.ToBase64String(x.RowVersion));
}

public sealed record GetAtaChapterCategoriesQuery(int Page = 1, int PageSize = 20, string? Search = null,
    bool? IsActive = null, string? Sort = null) : IQuery<PagedResult<AtaChapterCategoryDto>>;

public sealed class GetAtaChapterCategoriesQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetAtaChapterCategoriesQuery, PagedResult<AtaChapterCategoryDto>>
{
    public async Task<Result<PagedResult<AtaChapterCategoryDto>>> Handle(GetAtaChapterCategoriesQuery request, CancellationToken ct)
    {
        var paging = PageRequest.From(request.Page, request.PageSize);
        var query = db.AtaChapterCategories.AsNoTracking();
        if (request.IsActive is { } active)
            query = query.Where(x => x.IsActive == active);
        if (SearchFilter.Term(request.Search) is { } term)
            query = query.Where(x => x.Name.ToLower().Contains(term));

        var total = await query.LongCountAsync(ct);
        if (paging.IsOutOfRange(total))
            return paging.Empty<AtaChapterCategoryDto>(total);
        var sort = SortSpec.Parse(request.Sort);
        var sorted = sort?.Field switch
        {
            "isactive" => sort.Value.Descending ? query.OrderByDescending(x => x.IsActive) : query.OrderBy(x => x.IsActive),
            _ => sort?.Descending == true ? query.OrderByDescending(x => x.Name) : query.OrderBy(x => x.Name)
        };
        var items = await sorted.ThenBy(x => x.Id).Skip(paging.Skip).Take(paging.PageSize)
            .Select(AtaChapterProjections.Category).ToListAsync(ct);
        return paging.ToResult<AtaChapterCategoryDto>(items, total);
    }
}

public sealed record GetAtaChapterCategoryByIdQuery(Guid Id) : IQuery<AtaChapterCategoryDto>;
public sealed class GetAtaChapterCategoryByIdQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetAtaChapterCategoryByIdQuery, AtaChapterCategoryDto>
{
    public async Task<Result<AtaChapterCategoryDto>> Handle(GetAtaChapterCategoryByIdQuery request, CancellationToken ct)
    {
        var item = await db.AtaChapterCategories.AsNoTracking().Where(x => x.Id == request.Id)
            .Select(AtaChapterProjections.Category).FirstOrDefaultAsync(ct);
        return item is null ? Error.NotFound("ATA chapter category not found.", "MasterData.AtaChapterCategory.NotFound") : item;
    }
}

public sealed record GetActiveAtaChapterCategoryOptionsQuery : IQuery<IReadOnlyList<AtaChapterCategoryDto>>;
public sealed class GetActiveAtaChapterCategoryOptionsQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetActiveAtaChapterCategoryOptionsQuery, IReadOnlyList<AtaChapterCategoryDto>>
{
    public async Task<Result<IReadOnlyList<AtaChapterCategoryDto>>> Handle(GetActiveAtaChapterCategoryOptionsQuery request, CancellationToken ct)
    {
        IReadOnlyList<AtaChapterCategoryDto> items = await db.AtaChapterCategories.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).Select(AtaChapterProjections.Category).ToListAsync(ct);
        return Result.Success(items);
    }
}

public sealed record GetAtaChaptersQuery(int Page = 1, int PageSize = 20, string? Search = null,
    bool? IsActive = null, string? Sort = null, Guid? CategoryId = null, bool? EffectiveActive = null) : IQuery<PagedResult<AtaChapterDto>>;
public sealed class GetAtaChaptersQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetAtaChaptersQuery, PagedResult<AtaChapterDto>>
{
    public async Task<Result<PagedResult<AtaChapterDto>>> Handle(GetAtaChaptersQuery request, CancellationToken ct)
    {
        var paging = PageRequest.From(request.Page, request.PageSize);
        var query = db.AtaChapters.AsNoTracking();
        if (request.IsActive is { } active)
            query = query.Where(x => x.IsActive == active);
        if (request.EffectiveActive is { } effectiveActive)
            query = query.Where(x => (x.IsActive && x.Category.IsActive) == effectiveActive);
        if (request.CategoryId is { } categoryId)
            query = query.Where(x => x.CategoryId == categoryId);
        if (SearchFilter.Term(request.Search) is { } term)
            query = query.Where(x => x.Code.ToLower().Contains(term) || x.Title.ToLower().Contains(term) || x.Category.Name.ToLower().Contains(term));

        var total = await query.LongCountAsync(ct);
        if (paging.IsOutOfRange(total))
            return paging.Empty<AtaChapterDto>(total);
        var sort = SortSpec.Parse(request.Sort);
        var sorted = sort?.Field switch
        {
            "title" => sort.Value.Descending ? query.OrderByDescending(x => x.Title) : query.OrderBy(x => x.Title),
            "categoryname" => sort.Value.Descending ? query.OrderByDescending(x => x.Category.Name) : query.OrderBy(x => x.Category.Name),
            "isactive" => sort.Value.Descending ? query.OrderByDescending(x => x.IsActive) : query.OrderBy(x => x.IsActive),
            _ => sort?.Descending == true ? query.OrderByDescending(x => x.Code) : query.OrderBy(x => x.Code)
        };
        var items = await sorted.ThenBy(x => x.Id).Skip(paging.Skip).Take(paging.PageSize)
            .Select(AtaChapterProjections.Chapter).ToListAsync(ct);
        return paging.ToResult<AtaChapterDto>(items, total);
    }
}

public sealed record GetAtaChapterByIdQuery(Guid Id) : IQuery<AtaChapterDto>;
public sealed class GetAtaChapterByIdQueryHandler(IMasterDataDbContext db) : IQueryHandler<GetAtaChapterByIdQuery, AtaChapterDto>
{
    public async Task<Result<AtaChapterDto>> Handle(GetAtaChapterByIdQuery request, CancellationToken ct)
    {
        var item = await db.AtaChapters.AsNoTracking().Where(x => x.Id == request.Id)
            .Select(AtaChapterProjections.Chapter).FirstOrDefaultAsync(ct);
        return item is null ? Error.NotFound("ATA chapter not found.", "MasterData.AtaChapter.NotFound") : item;
    }
}

public sealed record GetActiveAtaChapterOptionsQuery : IQuery<IReadOnlyList<AtaChapterDto>>;
public sealed class GetActiveAtaChapterOptionsQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetActiveAtaChapterOptionsQuery, IReadOnlyList<AtaChapterDto>>
{
    public async Task<Result<IReadOnlyList<AtaChapterDto>>> Handle(GetActiveAtaChapterOptionsQuery request, CancellationToken ct)
    {
        IReadOnlyList<AtaChapterDto> items = await db.AtaChapters.AsNoTracking().Where(x => x.IsActive && x.Category.IsActive)
            .OrderBy(x => x.Code).ThenBy(x => x.Id).Select(AtaChapterProjections.Chapter).ToListAsync(ct);
        return Result.Success(items);
    }
}
