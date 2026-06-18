using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Contracts.Features.Employee;
using Core.Contracts.Features.License;
using Core.Contracts.Features.ManpowerType;
using Core.Contracts.Features.Station;
using Identity.Domain.Enumerations;
using Microsoft.EntityFrameworkCore;

namespace Core.Application.Features.Station.Queries.GetPaginatedStations;

public sealed class GetPaginatedStationsQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedStationsQuery, PaginatedResult<StationDto>>
{
    public async Task<Result<PaginatedResult<StationDto>>> Handle(
        GetPaginatedStationsQuery request,
        CancellationToken cancellationToken)
    {
        var stations = await db.Stations.ToListAsync(cancellationToken);
        var employees = await db.Employees.ToListAsync(cancellationToken);
        var manpowerTypes = await db.ManpowerTypes.ToListAsync(cancellationToken);
        var employeeLicenses = await db.EmployeeLicenses.ToListAsync(cancellationToken);
        var licenses = await db.Licenses.ToListAsync(cancellationToken);

        var manpowerTypeIndex = manpowerTypes.ToDictionary(mt => mt.Id);
        var stationIndex = stations.ToDictionary(s => s.Id);
        var licenseIndex = licenses.ToDictionary(l => l.Id);

        var query = stations.Select(s =>
        {
            var stSnapshot = new StationSnapshot(s.Id.Value, s.Name, s.IataCode.Value);

            var assignedEmployees = employees.Where(e => e.StationId == s.Id).Select(e =>
            {
                manpowerTypeIndex.TryGetValue(e.ManpowerTypeId, out var mt);
                var mtSnapshot = new ManpowerTypeSnapshot(e.ManpowerTypeId.Value, mt?.Name ?? string.Empty);

                var empLicenses = employeeLicenses
                    .Where(el => el.EmployeeId == e.Id)
                    .Select(el =>
                    {
                        licenseIndex.TryGetValue(el.LicenseId, out var lic);
                        var empSnapshot = new EmployeeSnapshot(e.Id.Value, e.FullName, stSnapshot, mtSnapshot);
                        var licSnapshot = new LicenseSnapshot(el.LicenseId.Value, lic?.Code ?? string.Empty);
                        return new EmployeeLicenseDto(empSnapshot, licSnapshot, el.LicenseNumber);
                    })
                    .ToList<EmployeeLicenseDto>();

                var userStatus = e.LinkedUserId is null
                    ? UserStatus.PendingActivation
                    : e.IsActive ? UserStatus.Active : UserStatus.Deactivated;

                return new EmployeeDto(
                    e.Id.Value,
                    e.FullName,
                    e.Email,
                    mtSnapshot,
                    stSnapshot,
                    e.Contract.From,
                    e.Contract.To,
                    e.WorkingSchedule.Days.ToList(),
                    e.LinkedUserId,
                    e.IsActive,
                    UserType.Employee,
                    userStatus,
                    e.CreatedAt,
                    null,
                    empLicenses);
            }).ToList<EmployeeDto>();

            return new StationDto(
                s.Id.Value,
                s.IataCode.Value,
                null,
                s.Name,
                s.City,
                s.IsActive,
                assignedEmployees,
                s.CreatedAt,
                s.UpdatedAt);
        }).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        var total = query.Count();

        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Name);

        var items = query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return new PaginatedResult<StationDto>(items, total, request.Page, request.PageSize);
    }
}
