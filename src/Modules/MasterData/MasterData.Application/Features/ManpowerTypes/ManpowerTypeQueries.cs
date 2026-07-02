using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using MasterData.Application.Abstractions;
using MasterData.Application.Contracts;
using MasterData.Domain.ManpowerTypes;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.ManpowerTypes;

// --- Paged list -----------------------------------------------------------

public sealed record GetManpowerTypesQuery(int Page = 1, int PageSize = 20, string? Search = null, bool? IsActive = null, string? Sort = null)
    : IQuery<PagedResult<ManpowerTypeListItemDto>>;

public sealed class GetManpowerTypesQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetManpowerTypesQuery, PagedResult<ManpowerTypeListItemDto>>
{
    public async Task<Result<PagedResult<ManpowerTypeListItemDto>>> Handle(GetManpowerTypesQuery request, CancellationToken cancellationToken)
    {
        var paging = PageRequest.From(request.Page, request.PageSize);

        var query = db.ManpowerTypes.AsNoTracking();

        if (request.IsActive is { } active)
            query = query.Where(m => m.IsActive == active);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(m => m.Name.Contains(term));
        }

        var total = await query.LongCountAsync(cancellationToken);
        if (paging.IsOutOfRange(total))
            return paging.Empty<ManpowerTypeListItemDto>(total);

        var items = await ApplySort(query, request.Sort)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(m => new ManpowerTypeListItemDto(m.Id, m.Name, m.Description, m.IsActive))
            .ToListAsync(cancellationToken);

        return paging.ToResult<ManpowerTypeListItemDto>(items, total);
    }

    private static IQueryable<ManpowerType> ApplySort(IQueryable<ManpowerType> query, string? sort)
    {
        if (SortSpec.Parse(sort) is not { } spec)
            return query.OrderBy(m => m.Name).ThenBy(m => m.Id);

        return spec.Field switch
        {
            "name" => spec.Descending ? query.OrderByDescending(m => m.Name).ThenByDescending(m => m.Id) : query.OrderBy(m => m.Name).ThenBy(m => m.Id),
            "isactive" => spec.Descending ? query.OrderByDescending(m => m.IsActive).ThenByDescending(m => m.Id) : query.OrderBy(m => m.IsActive).ThenBy(m => m.Id),
            _ => query.OrderBy(m => m.Name).ThenBy(m => m.Id)
        };
    }
}

// --- By id ----------------------------------------------------------------

public sealed record GetManpowerTypeByIdQuery(Guid Id) : IQuery<ManpowerTypeDto>;

public sealed class GetManpowerTypeByIdQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetManpowerTypeByIdQuery, ManpowerTypeDto>
{
    public async Task<Result<ManpowerTypeDto>> Handle(GetManpowerTypeByIdQuery request, CancellationToken cancellationToken)
    {
        var manpowerType = await db.ManpowerTypes.AsNoTracking().FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken);
        if (manpowerType is null)
            return Error.NotFound("Manpower type not found.", "MasterData.ManpowerType.NotFound");

        return new ManpowerTypeDto(
            manpowerType.Id, manpowerType.Name, manpowerType.Description, manpowerType.IsActive,
            manpowerType.CreatedAtUtc, manpowerType.UpdatedAtUtc, Convert.ToBase64String(manpowerType.RowVersion));
    }
}

// --- Active options (for pickers) -----------------------------------------

public sealed record GetActiveManpowerTypeOptionsQuery : IQuery<IReadOnlyList<ManpowerTypeOptionDto>>;

public sealed class GetActiveManpowerTypeOptionsQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetActiveManpowerTypeOptionsQuery, IReadOnlyList<ManpowerTypeOptionDto>>
{
    public async Task<Result<IReadOnlyList<ManpowerTypeOptionDto>>> Handle(GetActiveManpowerTypeOptionsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<ManpowerTypeOptionDto> options = await db.ManpowerTypes.AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.Name)
            .ThenBy(m => m.Id)
            .Select(m => new ManpowerTypeOptionDto(m.Id, m.Name))
            .ToListAsync(cancellationToken);

        return Result.Success(options);
    }
}
