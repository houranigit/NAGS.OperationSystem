using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using MasterData.Application.Abstractions;
using MasterData.Application.Contracts;
using MasterData.Domain.Licenses;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.Licenses;

// --- Paged list -----------------------------------------------------------

public sealed record GetLicensesQuery(int Page = 1, int PageSize = 20, string? Search = null, bool? IsActive = null, string? Sort = null)
    : IQuery<PagedResult<LicenseListItemDto>>;

public sealed class GetLicensesQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetLicensesQuery, PagedResult<LicenseListItemDto>>
{
    public async Task<Result<PagedResult<LicenseListItemDto>>> Handle(GetLicensesQuery request, CancellationToken cancellationToken)
    {
        var paging = PageRequest.From(request.Page, request.PageSize);

        var query = db.Licenses.AsNoTracking();

        if (request.IsActive is { } active)
            query = query.Where(l => l.IsActive == active);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            var upper = term.ToUpperInvariant();
            query = query.Where(l => l.Name.Contains(term) || l.Code.Contains(upper));
        }

        var total = await query.LongCountAsync(cancellationToken);
        if (paging.IsOutOfRange(total))
            return paging.Empty<LicenseListItemDto>(total);

        var items = await ApplySort(query, request.Sort)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(l => new LicenseListItemDto(l.Id, l.Code, l.Name, l.Description, l.IsActive))
            .ToListAsync(cancellationToken);

        return paging.ToResult<LicenseListItemDto>(items, total);
    }

    private static IQueryable<License> ApplySort(IQueryable<License> query, string? sort)
    {
        if (SortSpec.Parse(sort) is not { } spec)
            return query.OrderBy(l => l.Code).ThenBy(l => l.Id);

        return spec.Field switch
        {
            "code" => spec.Descending ? query.OrderByDescending(l => l.Code).ThenByDescending(l => l.Id) : query.OrderBy(l => l.Code).ThenBy(l => l.Id),
            "name" => spec.Descending ? query.OrderByDescending(l => l.Name).ThenByDescending(l => l.Id) : query.OrderBy(l => l.Name).ThenBy(l => l.Id),
            "isactive" => spec.Descending ? query.OrderByDescending(l => l.IsActive).ThenByDescending(l => l.Id) : query.OrderBy(l => l.IsActive).ThenBy(l => l.Id),
            _ => query.OrderBy(l => l.Code).ThenBy(l => l.Id)
        };
    }
}

// --- By id ----------------------------------------------------------------

public sealed record GetLicenseByIdQuery(Guid Id) : IQuery<LicenseDto>;

public sealed class GetLicenseByIdQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetLicenseByIdQuery, LicenseDto>
{
    public async Task<Result<LicenseDto>> Handle(GetLicenseByIdQuery request, CancellationToken cancellationToken)
    {
        var license = await db.Licenses.AsNoTracking().FirstOrDefaultAsync(l => l.Id == request.Id, cancellationToken);
        if (license is null)
            return Error.NotFound("License not found.", "MasterData.License.NotFound");

        return new LicenseDto(
            license.Id, license.Code, license.Name, license.Description, license.IsActive,
            license.CreatedAtUtc, license.UpdatedAtUtc, Convert.ToBase64String(license.RowVersion));
    }
}

// --- Active options (for pickers) -----------------------------------------

public sealed record GetActiveLicenseOptionsQuery : IQuery<IReadOnlyList<LicenseOptionDto>>;

public sealed class GetActiveLicenseOptionsQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetActiveLicenseOptionsQuery, IReadOnlyList<LicenseOptionDto>>
{
    public async Task<Result<IReadOnlyList<LicenseOptionDto>>> Handle(GetActiveLicenseOptionsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<LicenseOptionDto> options = await db.Licenses.AsNoTracking()
            .Where(l => l.IsActive)
            .OrderBy(l => l.Code)
            .ThenBy(l => l.Id)
            .Select(l => new LicenseOptionDto(l.Id, l.Code, l.Name))
            .ToListAsync(cancellationToken);

        return Result.Success(options);
    }
}
