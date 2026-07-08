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

        var paging = PageRequest.From(request.Page, request.PageSize);

        var query = db.StaffMembers.AsNoTracking();

        // Station staff only ever see their own station's members; customer contacts see none.
        if (!resolved.Value.IsAdministrator)
        {
            if (resolved.Value.StationId is not { } scopedStation)
                return paging.Empty<StaffMemberListItemDto>();
            query = query.Where(s => s.StationId == scopedStation);
        }

        if (request.IsActive is { } active)
            query = query.Where(s => s.IsActive == active);

        if (request.StationId is { } stationId)
            query = query.Where(s => s.StationId == stationId);

        if (request.ManpowerTypeId is { } manpowerTypeId)
            query = query.Where(s => s.ManpowerTypeId == manpowerTypeId);

        if (SearchFilter.Term(request.Search) is { } term)
            query = query.Where(s => s.FullName.ToLower().Contains(term) || s.EmployeeId.ToLower().Contains(term) || s.Email.ToLower().Contains(term));

        var total = await query.LongCountAsync(cancellationToken);
        if (paging.IsOutOfRange(total))
            return paging.Empty<StaffMemberListItemDto>(total);

        var items = await ApplySort(query, request.Sort)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(s => new StaffMemberListItemDto(
                s.Id, s.FullName, s.EmployeeId, s.Email, s.StationId,
                db.Stations.Where(st => st.Id == s.StationId).Select(st => st.IataCode).FirstOrDefault() ?? string.Empty,
                s.ManpowerTypeId,
                db.ManpowerTypes.Where(m => m.Id == s.ManpowerTypeId).Select(m => m.Name).FirstOrDefault() ?? string.Empty,
                s.IsActive))
            .ToListAsync(cancellationToken);

        return paging.ToResult<StaffMemberListItemDto>(items, total);
    }

    private static IQueryable<StaffMember> ApplySort(IQueryable<StaffMember> query, string? sort)
    {
        if (SortSpec.Parse(sort) is not { } spec)
            return query.OrderBy(s => s.FullName).ThenBy(s => s.Id);

        return spec.Field switch
        {
            "fullname" => spec.Descending ? query.OrderByDescending(s => s.FullName).ThenByDescending(s => s.Id) : query.OrderBy(s => s.FullName).ThenBy(s => s.Id),
            "employeeid" => spec.Descending ? query.OrderByDescending(s => s.EmployeeId).ThenByDescending(s => s.Id) : query.OrderBy(s => s.EmployeeId).ThenBy(s => s.Id),
            "email" => spec.Descending ? query.OrderByDescending(s => s.Email).ThenByDescending(s => s.Id) : query.OrderBy(s => s.Email).ThenBy(s => s.Id),
            "isactive" => spec.Descending ? query.OrderByDescending(s => s.IsActive).ThenByDescending(s => s.Id) : query.OrderBy(s => s.IsActive).ThenBy(s => s.Id),
            _ => query.OrderBy(s => s.FullName).ThenBy(s => s.Id)
        };
    }
}

// --- Active options (lightweight picker) -----------------------------------

/// <summary>
/// Active staff members as lightweight picker options for flight assignment forms.
/// Exposed under the reference view-options permission; station staff are always confined to
/// their own station regardless of <paramref name="StationId"/>.
/// </summary>
public sealed record GetActiveStaffMemberOptionsQuery(Guid? StationId = null)
    : IQuery<IReadOnlyList<StaffMemberOptionDto>>;

public sealed class GetActiveStaffMemberOptionsQueryHandler(IMasterDataDbContext db, IMasterDataScope scope)
    : IQueryHandler<GetActiveStaffMemberOptionsQuery, IReadOnlyList<StaffMemberOptionDto>>
{
    public async Task<Result<IReadOnlyList<StaffMemberOptionDto>>> Handle(GetActiveStaffMemberOptionsQuery request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure)
            return resolved.Error;

        var query = db.StaffMembers.AsNoTracking().Where(s => s.IsActive);

        // Station staff only ever see their own station's members; customer contacts see none.
        if (!resolved.Value.IsAdministrator)
        {
            if (resolved.Value.StationId is not { } scopedStation)
                return Result.Success<IReadOnlyList<StaffMemberOptionDto>>([]);
            query = query.Where(s => s.StationId == scopedStation);
        }

        if (request.StationId is { } stationId)
            query = query.Where(s => s.StationId == stationId);

        IReadOnlyList<StaffMemberOptionDto> options = await query
            .OrderBy(s => s.FullName).ThenBy(s => s.Id)
            .Select(s => new StaffMemberOptionDto(
                s.Id, s.FullName, s.EmployeeId, s.StationId,
                db.Stations.Where(st => st.Id == s.StationId).Select(st => st.IataCode).FirstOrDefault() ?? string.Empty,
                db.ManpowerTypes.Where(m => m.Id == s.ManpowerTypeId).Select(m => m.Name).FirstOrDefault() ?? string.Empty))
            .ToListAsync(cancellationToken);

        return Result.Success(options);
    }
}

// --- By id ----------------------------------------------------------------

public sealed record GetStaffMemberByIdQuery(Guid Id) : IQuery<StaffMemberDto>;

public sealed class GetStaffMemberByIdQueryHandler(IMasterDataDbContext db, IMasterDataScope scope)
    : IQueryHandler<GetStaffMemberByIdQuery, StaffMemberDto>
{
    public async Task<Result<StaffMemberDto>> Handle(GetStaffMemberByIdQuery request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure)
            return resolved.Error;

        var query = db.StaffMembers.AsNoTracking()
            .Where(s => s.Id == request.Id);

        if (!resolved.Value.IsAdministrator)
        {
            if (resolved.Value.StationId is not { } scopedStation)
                return ScopeForbidden();

            query = query.Where(s => s.StationId == scopedStation);
        }

        var staff = await query
            .Select(s => new
            {
                s.Id,
                s.FullName,
                s.EmployeeId,
                s.Email,
                s.PendingLoginEmail,
                s.LoginEmailChangeFailureReason,
                s.StationId,
                StationCode = db.Stations.Where(st => st.Id == s.StationId).Select(st => st.IataCode).FirstOrDefault() ?? string.Empty,
                StationName = db.Stations.Where(st => st.Id == s.StationId).Select(st => st.Name).FirstOrDefault() ?? string.Empty,
                s.ManpowerTypeId,
                ManpowerTypeName = db.ManpowerTypes.Where(m => m.Id == s.ManpowerTypeId).Select(m => m.Name).FirstOrDefault() ?? string.Empty,
                s.EmploymentStartDate,
                s.EmploymentEndDate,
                s.WorkingScheduleMask,
                s.LinkedUserId,
                s.PortalState,
                s.PortalFailureReason,
                s.IsActive,
                s.CreatedAtUtc,
                s.UpdatedAtUtc,
                s.RowVersion
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (staff is null)
            return resolved.Value.IsAdministrator
                ? Error.NotFound("Staff member not found.", "MasterData.StaffMember.NotFound")
                : ScopeForbidden();

        var licenses = await (
            from assignment in db.StaffMemberLicenses.AsNoTracking()
            join license in db.Licenses.AsNoTracking() on assignment.LicenseId equals license.Id
            where assignment.StaffMemberId == staff.Id
            orderby license.Code
            select new StaffMemberLicenseDto(
                assignment.Id,
                assignment.LicenseId,
                license.Code,
                license.Name,
                assignment.LicenseNumber))
            .ToListAsync(cancellationToken);

        var contract = staff.EmploymentStartDate is { } start
            ? new EmploymentContractDto(start, staff.EmploymentEndDate)
            : null;
        IReadOnlyList<DayOfWeek>? workingDays = staff.WorkingScheduleMask is { } mask
            ? WorkingSchedule.FromMask(mask).Days.ToList()
            : null;

        return new StaffMemberDto(
            staff.Id, staff.FullName, staff.EmployeeId, staff.Email, staff.PendingLoginEmail, staff.LoginEmailChangeFailureReason,
            staff.StationId, staff.StationCode, staff.StationName,
            staff.ManpowerTypeId, staff.ManpowerTypeName,
            contract, workingDays, staff.LinkedUserId, staff.PortalState.ToString(), staff.PortalFailureReason, staff.IsActive,
            staff.CreatedAtUtc, staff.UpdatedAtUtc, Convert.ToBase64String(staff.RowVersion),
            licenses);
    }

    private static Error ScopeForbidden() =>
        Error.Forbidden("This record is outside your data scope.", "MasterData.Scope.Forbidden");
}
