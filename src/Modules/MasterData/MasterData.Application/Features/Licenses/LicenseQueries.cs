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
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

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

        var items = await ApplySort(query, request.Sort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new LicenseListItemDto(l.Id, l.Code, l.Name, l.Description, l.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResult<LicenseListItemDto>(items, page, pageSize, total);
    }

    private static IQueryable<License> ApplySort(IQueryable<License> query, string? sort)
    {
        if (SortSpec.Parse(sort) is not { } spec)
            return query.OrderBy(l => l.Code);

        return spec.Field switch
        {
            "code" => spec.Descending ? query.OrderByDescending(l => l.Code) : query.OrderBy(l => l.Code),
            "name" => spec.Descending ? query.OrderByDescending(l => l.Name) : query.OrderBy(l => l.Name),
            "isactive" => spec.Descending ? query.OrderByDescending(l => l.IsActive) : query.OrderBy(l => l.IsActive),
            _ => query.OrderBy(l => l.Code)
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
            .Select(l => new LicenseOptionDto(l.Id, l.Code, l.Name))
            .ToListAsync(cancellationToken);

        return Result.Success(options);
    }
}
