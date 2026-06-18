using Core.Contracts.Features.Employee;
using Core.Contracts.Features.ManpowerType;
using Core.Contracts.Features.Station;
using Core.Contracts.Readers;
using Core.Domain.Aggregates.Employee;
using Core.Domain.Aggregates.ManpowerType;
using Core.Domain.Aggregates.Station;
using Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Core.Infrastructure.Readers;

internal sealed class EmployeeReader(CoreDbContext context) : IEmployeeReader
{
    public async Task<EmployeeSnapshot?> GetByIdAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        // EmployeeId.From throws on Guid.Empty — when called as part of an EF predicate the
        // exception surfaces inside the funcletizer with no useful stack frame for callers
        // (this is the AOG-claim crash reported on FlightEmployeeInvitedIntegrationEvent
        // where InviterEmployeeId is Guid.Empty for system-issued events).
        if (employeeId == Guid.Empty)
            return null;

        var entity = await context.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == EmployeeId.From(employeeId), cancellationToken);

        if (entity is null)
            return null;

        var station = await context.Stations
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == entity.StationId, cancellationToken);

        var mt = await context.ManpowerTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == entity.ManpowerTypeId, cancellationToken);

        if (station is null || mt is null)
            return null;

        return new EmployeeSnapshot(
            entity.Id.Value,
            entity.FullName,
            new StationSnapshot(station.Id.Value, station.Name, station.IataCode.Value),
            new ManpowerTypeSnapshot(mt.Id.Value, mt.Name));
    }

    public async Task<IReadOnlyList<EmployeeSearchResultDto>> SearchActiveByNameAsync(
        string search,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(search) || take < 1)
            return [];

        var term = search.Trim();
        var cap = Math.Clamp(take, 1, 100);

        return await context.Employees
            .AsNoTracking()
            .Where(e => e.IsActive && e.FullName.Contains(term))
            .OrderBy(e => e.FullName)
            .Take(cap)
            .Select(e => new EmployeeSearchResultDto(e.Id.Value, e.FullName, e.Email))
            .ToListAsync(cancellationToken);
    }

    public async Task<EmployeeSnapshot?> GetByLinkedUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return null;

        var entity = await context.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.LinkedUserId == userId && e.IsActive, cancellationToken);

        if (entity is null)
            return null;

        var station = await context.Stations
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == entity.StationId, cancellationToken);

        var mt = await context.ManpowerTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == entity.ManpowerTypeId, cancellationToken);

        if (station is null || mt is null)
            return null;

        return new EmployeeSnapshot(
            entity.Id.Value,
            entity.FullName,
            new StationSnapshot(station.Id.Value, station.Name, station.IataCode.Value),
            new ManpowerTypeSnapshot(mt.Id.Value, mt.Name));
    }

    public async Task<Guid?> GetLinkedUserIdByEmployeeIdAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        if (employeeId == Guid.Empty)
            return null;

        return await context.Employees
            .AsNoTracking()
            .Where(e => e.Id == EmployeeId.From(employeeId))
            .Select(e => e.LinkedUserId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EmployeeSearchResultDto>> SearchActiveByStationAsync(
        Guid stationId,
        string? search,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (stationId == Guid.Empty || take < 1)
            return [];

        var cap = Math.Clamp(take, 1, 100);
        var query = context.Employees
            .AsNoTracking()
            .Where(e => e.IsActive && e.StationId == StationId.From(stationId));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(e => e.FullName.Contains(term));
        }

        return await query
            .OrderBy(e => e.FullName)
            .Take(cap)
            .Select(e => new EmployeeSearchResultDto(e.Id.Value, e.FullName, e.Email))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EmployeeSnapshot>> SearchActiveSnapshotsByStationAsync(
        Guid stationId,
        string? search,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (stationId == Guid.Empty || take < 1)
            return [];

        var cap = Math.Clamp(take, 1, 100);
        var query = context.Employees
            .AsNoTracking()
            .Where(e => e.IsActive && e.StationId == StationId.From(stationId));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(e => e.FullName.Contains(term));
        }

        // Single round-trip — join station + manpower in one projection so the
        // mobile invite list always renders the role / station chips with no
        // follow-up calls.
        return await query
            .OrderBy(e => e.FullName)
            .Take(cap)
            .Join(
                context.Stations.AsNoTracking(),
                e => e.StationId,
                s => s.Id,
                (e, s) => new { Employee = e, Station = s })
            .Join(
                context.ManpowerTypes.AsNoTracking(),
                pair => pair.Employee.ManpowerTypeId,
                m => m.Id,
                (pair, m) => new EmployeeSnapshot(
                    pair.Employee.Id.Value,
                    pair.Employee.FullName,
                    new StationSnapshot(pair.Station.Id.Value, pair.Station.Name, pair.Station.IataCode.Value),
                    new ManpowerTypeSnapshot(m.Id.Value, m.Name)))
            .ToListAsync(cancellationToken);
    }
}
