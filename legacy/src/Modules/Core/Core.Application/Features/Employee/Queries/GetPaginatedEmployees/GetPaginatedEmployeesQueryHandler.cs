using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Contracts.Features.Employee;
using Core.Contracts.Features.License;
using Core.Contracts.Features.ManpowerType;
using Core.Contracts.Features.Station;
using Core.Domain.ValueObjects;
using Identity.Domain.Enumerations;
using Microsoft.EntityFrameworkCore;

namespace Core.Application.Features.Employee.Queries.GetPaginatedEmployees;

/// <summary>
/// Paginated grid query for employees — stays on <see cref="IQueryable{T}"/> until one terminal materialization,
/// mirroring <see cref="Customer.Queries.GetPaginatedCustomers.GetPaginatedCustomersQueryHandler"/>.
/// </summary>
/// <remarks>
/// Related rows (manpower type, station, licenses + license codes) are composed inside EF <c>Select</c>/<c>Join</c>
/// so paging applies on the employee root. Working-day enums are expanded from the persisted schedule bitmask in memory
/// after the query returns (same bitmask as <see cref="WorkingSchedule"/>).
/// </remarks>
public sealed class GetPaginatedEmployeesQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedEmployeesQuery, PaginatedResult<EmployeeDto>>
{
    public async Task<Result<PaginatedResult<EmployeeDto>>> Handle(
        GetPaginatedEmployeesQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Employees.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(e => e.FullName);

        var pageRows = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Join(db.ManpowerTypes, e => e.ManpowerTypeId, mt => mt.Id, (e, mt) => new { e, mt })
            .Join(db.Stations, x => x.e.StationId, st => st.Id, (x, st) => new { x.e, x.mt, st })
            .Select(x => new EmployeePageRow(
                x.e.Id.Value,
                x.e.FullName,
                x.e.Email,
                x.e.ManpowerTypeId.Value,
                x.mt.Name,
                x.e.StationId.Value,
                x.st.Name,
                x.st.IataCode.Value,
                x.e.Contract.From,
                x.e.Contract.To,
                x.e.WorkingSchedule.Mask,
                x.e.LinkedUserId,
                x.e.IsActive,
                x.e.CreatedAt,
                x.e.Licenses.Select(el => new EmployeeLicensePageRow(
                        el.LicenseId.Value,
                        el.LicenseNumber,
                        db.Licenses.Where(l => l.Id == el.LicenseId).Select(l => l.Code).FirstOrDefault() ?? ""))
                    .ToList()))
            .ToListAsync(cancellationToken);

        var items = pageRows.Select(ToDto).ToList();

        return new PaginatedResult<EmployeeDto>(items, total, request.Page, request.PageSize);
    }

    private static EmployeeDto ToDto(EmployeePageRow r)
    {
        var workingDays = WorkingSchedule.FromMask(r.WorkingScheduleMask).Days.OrderBy(d => d).ToList();

        var mtSnap = new ManpowerTypeSnapshot(r.ManpowerTypeId, r.ManpowerTypeName);
        var stSnap = new StationSnapshot(r.StationId, r.StationName, r.StationIata);

        var userStatus = r.LinkedUserId is null
            ? UserStatus.PendingActivation
            : r.IsActive ? UserStatus.Active : UserStatus.Deactivated;

        var empSnap = new EmployeeSnapshot(r.Id, r.FullName, stSnap, mtSnap);

        var licenses = r.Licenses
            .Select(l => new EmployeeLicenseDto(
                empSnap,
                new LicenseSnapshot(l.LicenseId, l.LicenseCode),
                l.LicenseNumber))
            .ToList();

        return new EmployeeDto(
            r.Id,
            r.FullName,
            r.Email,
            mtSnap,
            stSnap,
            r.ContractFrom,
            r.ContractTo,
            workingDays,
            r.LinkedUserId,
            r.IsActive,
            UserType.Employee,
            userStatus,
            r.CreatedAt,
            null,
            licenses);
    }

    private sealed record EmployeeLicensePageRow(Guid LicenseId, string LicenseNumber, string LicenseCode);

    private sealed record EmployeePageRow(
        Guid Id,
        string FullName,
        string Email,
        Guid ManpowerTypeId,
        string ManpowerTypeName,
        Guid StationId,
        string StationName,
        string StationIata,
        DateOnly? ContractFrom,
        DateOnly? ContractTo,
        int WorkingScheduleMask,
        Guid? LinkedUserId,
        bool IsActive,
        DateTime CreatedAt,
        List<EmployeeLicensePageRow> Licenses);
}
