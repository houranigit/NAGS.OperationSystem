using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using MasterData.Application.Abstractions;
using MasterData.Application.Contracts;
using MasterData.Domain.AircraftTypes;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.AircraftTypes;

public sealed record GetAircraftTypesQuery(int Page = 1, int PageSize = 20, string? Search = null, bool? IsActive = null, string? Sort = null)
    : IQuery<PagedResult<AircraftTypeListItemDto>>;

public sealed class GetAircraftTypesQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetAircraftTypesQuery, PagedResult<AircraftTypeListItemDto>>
{
    public async Task<Result<PagedResult<AircraftTypeListItemDto>>> Handle(GetAircraftTypesQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var query = db.AircraftTypes.AsNoTracking();

        if (request.IsActive is { } active)
            query = query.Where(a => a.IsActive == active);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToUpperInvariant();
            query = query.Where(a => a.Model.Contains(term) || (a.Notes != null && a.Notes.Contains(request.Search.Trim())));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await ApplySort(query, request.Sort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AircraftTypeListItemDto(a.Id, a.Manufacturer, a.Model, a.Notes, a.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResult<AircraftTypeListItemDto>(items, page, pageSize, total);
    }

    private static IQueryable<AircraftType> ApplySort(IQueryable<AircraftType> query, string? sort)
    {
        if (SortSpec.Parse(sort) is not { } spec)
            return query.OrderBy(a => a.Manufacturer).ThenBy(a => a.Model);

        return spec.Field switch
        {
            "manufacturer" => spec.Descending ? query.OrderByDescending(a => a.Manufacturer) : query.OrderBy(a => a.Manufacturer),
            "model" => spec.Descending ? query.OrderByDescending(a => a.Model) : query.OrderBy(a => a.Model),
            "isactive" => spec.Descending ? query.OrderByDescending(a => a.IsActive) : query.OrderBy(a => a.IsActive),
            _ => query.OrderBy(a => a.Manufacturer).ThenBy(a => a.Model)
        };
    }
}

public sealed record GetAircraftTypeByIdQuery(Guid Id) : IQuery<AircraftTypeDto>;

public sealed class GetAircraftTypeByIdQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetAircraftTypeByIdQuery, AircraftTypeDto>
{
    public async Task<Result<AircraftTypeDto>> Handle(GetAircraftTypeByIdQuery request, CancellationToken cancellationToken)
    {
        var aircraftType = await db.AircraftTypes.AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);
        if (aircraftType is null)
            return Error.NotFound("Aircraft type not found.", "MasterData.AircraftType.NotFound");

        return new AircraftTypeDto(
            aircraftType.Id, aircraftType.Manufacturer, aircraftType.Model, aircraftType.Notes, aircraftType.IsActive,
            aircraftType.CreatedAtUtc, aircraftType.UpdatedAtUtc, Convert.ToBase64String(aircraftType.RowVersion));
    }
}

public sealed record GetActiveAircraftTypeOptionsQuery : IQuery<IReadOnlyList<AircraftTypeOptionDto>>;

public sealed class GetActiveAircraftTypeOptionsQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetActiveAircraftTypeOptionsQuery, IReadOnlyList<AircraftTypeOptionDto>>
{
    public async Task<Result<IReadOnlyList<AircraftTypeOptionDto>>> Handle(GetActiveAircraftTypeOptionsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<AircraftTypeOptionDto> options = await db.AircraftTypes.AsNoTracking()
            .Where(a => a.IsActive)
            .OrderBy(a => a.Manufacturer)
            .ThenBy(a => a.Model)
            .Select(a => new AircraftTypeOptionDto(a.Id, a.Manufacturer, a.Model))
            .ToListAsync(cancellationToken);

        return Result.Success(options);
    }
}
