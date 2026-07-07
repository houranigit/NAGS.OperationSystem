using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using MasterData.Application.Abstractions;
using MasterData.Application.Authorization;
using MasterData.Application.Contracts;
using MasterData.Domain.Stations;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.Stations;

// --- Paged list -----------------------------------------------------------

public sealed record GetStationsQuery(int Page = 1, int PageSize = 20, string? Search = null, bool? IsActive = null, Guid? CountryId = null, string? Sort = null)
    : IQuery<PagedResult<StationListItemDto>>;

public sealed class GetStationsQueryHandler(IMasterDataDbContext db, IMasterDataScope scope)
    : IQueryHandler<GetStationsQuery, PagedResult<StationListItemDto>>
{
    public async Task<Result<PagedResult<StationListItemDto>>> Handle(GetStationsQuery request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure)
            return resolved.Error;

        var paging = PageRequest.From(request.Page, request.PageSize);

        var query = db.Stations.AsNoTracking();

        // Station staff only see their own station; customer contacts have no station scope.
        if (!resolved.Value.IsAdministrator)
        {
            if (resolved.Value.StationId is not { } scopedStation)
                return paging.Empty<StationListItemDto>();
            query = query.Where(s => s.Id == scopedStation);
        }

        if (request.IsActive is { } active)
            query = query.Where(s => s.IsActive == active);

        if (request.CountryId is { } countryId)
            query = query.Where(s => s.CountryId == countryId);

        if (SearchFilter.Term(request.Search) is { } term)
        {
            var matchesId = Guid.TryParse(request.Search!.Trim(), out var stationId);

            query = query.Where(s =>
                (matchesId && s.Id == stationId) ||
                s.Name.ToLower().Contains(term) ||
                (s.City != null && s.City.ToLower().Contains(term)) ||
                s.IataCode.ToLower().Contains(term) ||
                (s.IcaoCode != null && s.IcaoCode.ToLower().Contains(term)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        if (paging.IsOutOfRange(total))
            return paging.Empty<StationListItemDto>(total);

        var items = await ApplySort(query, request.Sort)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(s => new StationListItemDto(
                s.Id, s.IataCode, s.IcaoCode, s.Name, s.City, s.CountryId,
                db.Countries.Where(c => c.Id == s.CountryId).Select(c => c.Name).FirstOrDefault() ?? string.Empty,
                s.IsActive))
            .ToListAsync(cancellationToken);

        return paging.ToResult<StationListItemDto>(items, total);
    }

    private static IQueryable<Station> ApplySort(IQueryable<Station> query, string? sort)
    {
        if (SortSpec.Parse(sort) is not { } spec)
            return query.OrderBy(s => s.IataCode).ThenBy(s => s.Id);

        return spec.Field switch
        {
            "iatacode" => spec.Descending ? query.OrderByDescending(s => s.IataCode).ThenByDescending(s => s.Id) : query.OrderBy(s => s.IataCode).ThenBy(s => s.Id),
            "name" => spec.Descending ? query.OrderByDescending(s => s.Name).ThenByDescending(s => s.Id) : query.OrderBy(s => s.Name).ThenBy(s => s.Id),
            "city" => spec.Descending ? query.OrderByDescending(s => s.City).ThenByDescending(s => s.Id) : query.OrderBy(s => s.City).ThenBy(s => s.Id),
            "isactive" => spec.Descending ? query.OrderByDescending(s => s.IsActive).ThenByDescending(s => s.Id) : query.OrderBy(s => s.IsActive).ThenBy(s => s.Id),
            _ => query.OrderBy(s => s.IataCode).ThenBy(s => s.Id)
        };
    }
}

// --- By id ----------------------------------------------------------------

public sealed record GetStationByIdQuery(Guid Id) : IQuery<StationDto>;

public sealed class GetStationByIdQueryHandler(IMasterDataDbContext db, IMasterDataScope scope)
    : IQueryHandler<GetStationByIdQuery, StationDto>
{
    public async Task<Result<StationDto>> Handle(GetStationByIdQuery request, CancellationToken cancellationToken)
    {
        var scopeCheck = await scope.CheckStationAsync(request.Id, cancellationToken);
        if (scopeCheck.IsFailure)
            return scopeCheck.Error;

        var station = await db.Stations.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);
        if (station is null)
            return Error.NotFound("Station not found.", "MasterData.Station.NotFound");

        var countryName = await db.Countries.AsNoTracking()
            .Where(c => c.Id == station.CountryId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        return new StationDto(
            station.Id, station.IataCode, station.IcaoCode, station.Name, station.City,
            station.CountryId, countryName, station.IsActive,
            station.CreatedAtUtc, station.UpdatedAtUtc, Convert.ToBase64String(station.RowVersion));
    }
}

// --- Active options (for pickers) -----------------------------------------

public sealed record GetActiveStationOptionsQuery : IQuery<IReadOnlyList<StationOptionDto>>;

public sealed class GetActiveStationOptionsQueryHandler(IMasterDataDbContext db, IMasterDataScope scope)
    : IQueryHandler<GetActiveStationOptionsQuery, IReadOnlyList<StationOptionDto>>
{
    public async Task<Result<IReadOnlyList<StationOptionDto>>> Handle(GetActiveStationOptionsQuery request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure)
            return resolved.Error;

        var query = db.Stations.AsNoTracking().Where(s => s.IsActive);

        if (!resolved.Value.IsAdministrator)
        {
            if (resolved.Value.StationId is not { } scopedStation)
                return Result.Success<IReadOnlyList<StationOptionDto>>([]);
            query = query.Where(s => s.Id == scopedStation);
        }

        IReadOnlyList<StationOptionDto> options = await query
            .OrderBy(s => s.IataCode)
            .ThenBy(s => s.Id)
            .Select(s => new StationOptionDto(s.Id, s.IataCode, s.Name))
            .ToListAsync(cancellationToken);

        return Result.Success(options);
    }
}
