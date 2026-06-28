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

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = db.Stations.AsNoTracking();

        // Station staff only see their own station; customer contacts have no station scope.
        if (!resolved.Value.IsAdministrator)
        {
            if (resolved.Value.StationId is not { } scopedStation)
                return new PagedResult<StationListItemDto>([], page, pageSize, 0);
            query = query.Where(s => s.Id == scopedStation);
        }

        if (request.IsActive is { } active)
            query = query.Where(s => s.IsActive == active);

        if (request.CountryId is { } countryId)
            query = query.Where(s => s.CountryId == countryId);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            var upper = term.ToUpperInvariant();
            var matchesId = Guid.TryParse(term, out var stationId);

            query = query.Where(s =>
                (matchesId && s.Id == stationId) ||
                s.Name.Contains(term) ||
                (s.City != null && s.City.Contains(term)) ||
                s.IataCode.Contains(upper) ||
                (s.IcaoCode != null && s.IcaoCode.Contains(upper)));
        }

        var total = await query.LongCountAsync(cancellationToken);

        var items = await ApplySort(query, request.Sort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new StationListItemDto(
                s.Id, s.IataCode, s.IcaoCode, s.Name, s.City, s.CountryId,
                db.Countries.Where(c => c.Id == s.CountryId).Select(c => c.Name).FirstOrDefault() ?? string.Empty,
                s.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResult<StationListItemDto>(items, page, pageSize, total);
    }

    private static IQueryable<Station> ApplySort(IQueryable<Station> query, string? sort)
    {
        if (SortSpec.Parse(sort) is not { } spec)
            return query.OrderBy(s => s.IataCode);

        return spec.Field switch
        {
            "iatacode" => spec.Descending ? query.OrderByDescending(s => s.IataCode) : query.OrderBy(s => s.IataCode),
            "name" => spec.Descending ? query.OrderByDescending(s => s.Name) : query.OrderBy(s => s.Name),
            "city" => spec.Descending ? query.OrderByDescending(s => s.City) : query.OrderBy(s => s.City),
            "isactive" => spec.Descending ? query.OrderByDescending(s => s.IsActive) : query.OrderBy(s => s.IsActive),
            _ => query.OrderBy(s => s.IataCode)
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
            .Select(s => new StationOptionDto(s.Id, s.IataCode, s.Name))
            .ToListAsync(cancellationToken);

        return Result.Success(options);
    }
}
