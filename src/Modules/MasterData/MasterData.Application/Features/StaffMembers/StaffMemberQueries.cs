using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using MasterData.Application.Abstractions;
using MasterData.Application.Authorization;
using MasterData.Application.Contracts;
using MasterData.Domain.StaffMembers;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.StaffMembers;

// --- Paged list -----------------------------------------------------------

public sealed record GetStaffMembersQuery(
    int Page = 1, int PageSize = 20, string? Search = null, bool? IsActive = null,
    Guid? StationId = null, Guid? ManpowerTypeId = null, string? Sort = null)
    : IQuery<PagedResult<StaffMemberListItemDto>>;

public sealed class GetStaffMembersQueryHandler(IMasterDataDbContext db, IMasterDataScope scope)
    : IQueryHandler<GetStaffMembersQuery, PagedResult<StaffMemberListItemDto>>
{
    public async Task<Result<PagedResult<StaffMemberListItemDto>>> Handle(GetStaffMembersQuery request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure)
            return resolved.Error;

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = db.StaffMembers.AsNoTracking();

        // Station staff only ever see their own station's members; customer contacts see none.
        if (!resolved.Value.IsAdministrator)
        {
            if (resolved.Value.StationId is not { } scopedStation)
                return new PagedResult<StaffMemberListItemDto>([], page, pageSize, 0);
            query = query.Where(s => s.StationId == scopedStation);
        }

        if (request.IsActive is { } active)
            query = query.Where(s => s.IsActive == active);

        if (request.StationId is { } stationId)
            query = query.Where(s => s.StationId == stationId);

        if (request.ManpowerTypeId is { } manpowerTypeId)
            query = query.Where(s => s.ManpowerTypeId == manpowerTypeId);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(s => s.FullName.Contains(term) || s.Email.Contains(term));
        }

        var total = await query.LongCountAsync(cancellationToken);

        var items = await ApplySort(query, request.Sort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new StaffMemberListItemDto(
                s.Id, s.FullName, s.Email, s.StationId,
                db.Stations.Where(st => st.Id == s.StationId).Select(st => st.IataCode).FirstOrDefault() ?? string.Empty,
                s.ManpowerTypeId,
                db.ManpowerTypes.Where(m => m.Id == s.ManpowerTypeId).Select(m => m.Name).FirstOrDefault() ?? string.Empty,
                s.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResult<StaffMemberListItemDto>(items, page, pageSize, total);
    }

    private static IQueryable<StaffMember> ApplySort(IQueryable<StaffMember> query, string? sort)
    {
        if (SortSpec.Parse(sort) is not { } spec)
            return query.OrderBy(s => s.FullName);

        return spec.Field switch
        {
            "fullname" => spec.Descending ? query.OrderByDescending(s => s.FullName) : query.OrderBy(s => s.FullName),
            "email" => spec.Descending ? query.OrderByDescending(s => s.Email) : query.OrderBy(s => s.Email),
            "isactive" => spec.Descending ? query.OrderByDescending(s => s.IsActive) : query.OrderBy(s => s.IsActive),
            _ => query.OrderBy(s => s.FullName)
        };
    }
}

// --- By id ----------------------------------------------------------------

public sealed record GetStaffMemberByIdQuery(Guid Id) : IQuery<StaffMemberDto>;

public sealed class GetStaffMemberByIdQueryHandler(IMasterDataDbContext db, IMasterDataScope scope)
    : IQueryHandler<GetStaffMemberByIdQuery, StaffMemberDto>
{
    public async Task<Result<StaffMemberDto>> Handle(GetStaffMemberByIdQuery request, CancellationToken cancellationToken)
    {
        var staff = await db.StaffMembers.AsNoTracking()
            .Include(s => s.Licenses)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);
        if (staff is null)
            return Error.NotFound("Staff member not found.", "MasterData.StaffMember.NotFound");

        var scopeCheck = await scope.CheckStationAsync(staff.StationId, cancellationToken);
        if (scopeCheck.IsFailure)
            return scopeCheck.Error;

        var station = await db.Stations.AsNoTracking()
            .Where(s => s.Id == staff.StationId)
            .Select(s => new { s.IataCode, s.Name })
            .FirstOrDefaultAsync(cancellationToken);

        var manpowerTypeName = await db.ManpowerTypes.AsNoTracking()
            .Where(m => m.Id == staff.ManpowerTypeId)
            .Select(m => m.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var licenseIds = staff.Licenses.Select(l => l.LicenseId).ToList();
        var licenseLookup = await db.Licenses.AsNoTracking()
            .Where(l => licenseIds.Contains(l.Id))
            .Select(l => new { l.Id, l.Code, l.Name })
            .ToListAsync(cancellationToken);

        var licenses = staff.Licenses
            .Select(l =>
            {
                var info = licenseLookup.FirstOrDefault(x => x.Id == l.LicenseId);
                return new StaffMemberLicenseDto(l.Id, l.LicenseId, info?.Code ?? string.Empty, info?.Name ?? string.Empty, l.LicenseNumber);
            })
            .OrderBy(l => l.LicenseCode)
            .ToList();

        var contract = staff.EmploymentContract is { } ec ? new EmploymentContractDto(ec.StartDate, ec.EndDate) : null;
        IReadOnlyList<DayOfWeek>? workingDays = staff.WorkingSchedule?.Days.ToList();

        return new StaffMemberDto(
            staff.Id, staff.FullName, staff.Email,
            staff.StationId, station?.IataCode ?? string.Empty, station?.Name ?? string.Empty,
            staff.ManpowerTypeId, manpowerTypeName,
            contract, workingDays, staff.LinkedUserId, staff.PortalState.ToString(), staff.PortalFailureReason, staff.IsActive,
            staff.CreatedAtUtc, staff.UpdatedAtUtc, Convert.ToBase64String(staff.RowVersion),
            licenses);
    }
}
