using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using MasterData.Application.Abstractions;
using MasterData.Application.Contracts;
using MasterData.Domain.Countries;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.Countries;

// --- Paged list -----------------------------------------------------------

public sealed record GetCountriesQuery(int Page = 1, int PageSize = 20, string? Search = null, bool? IsActive = null, string? Sort = null)
    : IQuery<PagedResult<CountryListItemDto>>;

public sealed class GetCountriesQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetCountriesQuery, PagedResult<CountryListItemDto>>
{
    public async Task<Result<PagedResult<CountryListItemDto>>> Handle(GetCountriesQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = db.Countries.AsNoTracking();

        if (request.IsActive is { } active)
            query = query.Where(c => c.IsActive == active);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            var upper = term.ToUpperInvariant();
            query = query.Where(c => c.Name.Contains(term) || c.IsoCode.Contains(upper));
        }

        var total = await query.LongCountAsync(cancellationToken);

        var items = await ApplySort(query, request.Sort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CountryListItemDto(c.Id, c.Name, c.IsoCode, c.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResult<CountryListItemDto>(items, page, pageSize, total);
    }

    private static IQueryable<Country> ApplySort(IQueryable<Country> query, string? sort)
    {
        if (SortSpec.Parse(sort) is not { } spec)
            return query.OrderBy(c => c.Name);

        return spec.Field switch
        {
            "name" => spec.Descending ? query.OrderByDescending(c => c.Name) : query.OrderBy(c => c.Name),
            "isocode" => spec.Descending ? query.OrderByDescending(c => c.IsoCode) : query.OrderBy(c => c.IsoCode),
            "isactive" => spec.Descending ? query.OrderByDescending(c => c.IsActive) : query.OrderBy(c => c.IsActive),
            _ => query.OrderBy(c => c.Name)
        };
    }
}

// --- By id ----------------------------------------------------------------

public sealed record GetCountryByIdQuery(Guid Id) : IQuery<CountryDto>;

public sealed class GetCountryByIdQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetCountryByIdQuery, CountryDto>
{
    public async Task<Result<CountryDto>> Handle(GetCountryByIdQuery request, CancellationToken cancellationToken)
    {
        var country = await db.Countries.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);
        if (country is null)
            return Error.NotFound("Country not found.", "MasterData.Country.NotFound");

        return new CountryDto(
            country.Id, country.Name, country.IsoCode, country.IsActive,
            country.CreatedAtUtc, country.UpdatedAtUtc, Convert.ToBase64String(country.RowVersion));
    }
}

// --- Active options (for pickers) -----------------------------------------

public sealed record GetActiveCountryOptionsQuery : IQuery<IReadOnlyList<CountryOptionDto>>;

public sealed class GetActiveCountryOptionsQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetActiveCountryOptionsQuery, IReadOnlyList<CountryOptionDto>>
{
    public async Task<Result<IReadOnlyList<CountryOptionDto>>> Handle(GetActiveCountryOptionsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<CountryOptionDto> options = await db.Countries.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new CountryOptionDto(c.Id, c.Name, c.IsoCode))
            .ToListAsync(cancellationToken);

        return Result.Success(options);
    }
}
