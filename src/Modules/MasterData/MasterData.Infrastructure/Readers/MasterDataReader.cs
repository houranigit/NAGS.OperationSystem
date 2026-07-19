using MasterData.Contracts.Readers;
using MasterData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Infrastructure.Readers;

/// <summary>
/// EF-backed implementation of the cross-module MasterData read seam. All reads are no-tracking
/// projections against the MasterData store; callers snapshot the results into their own aggregates.
/// </summary>
public sealed class MasterDataReader(MasterDataDbContext db) : IMasterDataReader
{
    public Task<CustomerReadSnapshot?> GetCustomerAsync(Guid id, CancellationToken cancellationToken) =>
        db.Customers.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CustomerReadSnapshot(c.Id, c.IataCode, c.IcaoCode, c.Name, c.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<StationReadSnapshot?> GetStationAsync(Guid id, CancellationToken cancellationToken) =>
        db.Stations.AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new StationReadSnapshot(s.Id, s.IataCode, s.IcaoCode, s.Name, s.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<OperationTypeReadSnapshot?> GetOperationTypeAsync(Guid id, CancellationToken cancellationToken) =>
        db.OperationTypes.AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => new OperationTypeReadSnapshot(o.Id, o.Name, o.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<AircraftTypeReadSnapshot?> GetAircraftTypeAsync(Guid id, CancellationToken cancellationToken) =>
        db.AircraftTypes.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new AircraftTypeReadSnapshot(a.Id, a.Manufacturer.ToString(), a.Model, a.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<ServiceReadSnapshot?> GetServiceAsync(Guid id, CancellationToken cancellationToken) =>
        db.Services.AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new ServiceReadSnapshot(s.Id, s.Name, s.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ServiceReadSnapshot>> GetServicesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return [];

        return await db.Services.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new ServiceReadSnapshot(s.Id, s.Name, s.IsActive))
            .ToListAsync(cancellationToken);
    }

    public Task<StaffMemberReadSnapshot?> GetStaffMemberAsync(Guid id, CancellationToken cancellationToken) =>
        db.StaffMembers.AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new StaffMemberReadSnapshot(s.Id, s.FullName, s.EmployeeId, s.StationId, s.ManpowerTypeId, s.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<StaffMemberReadSnapshot>> GetStaffMembersAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return [];

        return await db.StaffMembers.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new StaffMemberReadSnapshot(s.Id, s.FullName, s.EmployeeId, s.StationId, s.ManpowerTypeId, s.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StaffMemberReadSnapshot>> GetActiveStaffMembersForStationAsync(Guid stationId, CancellationToken cancellationToken)
    {
        return await db.StaffMembers.AsNoTracking()
            .Where(s => s.StationId == stationId && s.IsActive)
            .OrderBy(s => s.FullName)
            .ThenBy(s => s.EmployeeId)
            .Select(s => new StaffMemberReadSnapshot(s.Id, s.FullName, s.EmployeeId, s.StationId, s.ManpowerTypeId, s.IsActive))
            .ToListAsync(cancellationToken);
    }

    public Task<ToolReadSnapshot?> GetToolAsync(Guid id, CancellationToken cancellationToken) =>
        db.Tools.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new ToolReadSnapshot(t.Id, t.Name, t.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<MaterialReadSnapshot?> GetMaterialAsync(Guid id, CancellationToken cancellationToken) =>
        db.Materials.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new MaterialReadSnapshot(m.Id, m.Name, m.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<GeneralSupportReadSnapshot?> GetGeneralSupportAsync(Guid id, CancellationToken cancellationToken) =>
        db.GeneralSupports.AsNoTracking()
            .Where(g => g.Id == id)
            .Select(g => new GeneralSupportReadSnapshot(g.Id, g.Name, g.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<ManpowerTypeReadSnapshot?> GetManpowerTypeAsync(Guid id, CancellationToken cancellationToken) =>
        db.ManpowerTypes.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new ManpowerTypeReadSnapshot(m.Id, m.Name, m.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetAllowedActiveServiceIdsAsync(
        Guid manpowerTypeId,
        CancellationToken cancellationToken)
    {
        var manpowerTypeActive = await db.ManpowerTypes.AsNoTracking()
            .AnyAsync(type => type.Id == manpowerTypeId && type.IsActive, cancellationToken);
        if (!manpowerTypeActive)
            return new HashSet<Guid>();

        var serviceIds = await db.ManpowerTypeAllowedServices.AsNoTracking()
            .Where(allowance => allowance.ManpowerTypeId == manpowerTypeId)
            .Where(allowance => db.Services.Any(service =>
                service.Id == allowance.ServiceId &&
                service.IsActive &&
                service.Id != MasterData.Contracts.Seeding.WellKnownMasterDataIds.AircraftPerLandingService))
            .Select(allowance => allowance.ServiceId)
            .ToListAsync(cancellationToken);

        return serviceIds.ToHashSet();
    }

    public async Task<IReadOnlyList<ServiceReadSnapshot>> GetActiveServicesAsync(CancellationToken cancellationToken) =>
        await db.Services.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new ServiceReadSnapshot(s.Id, s.Name, s.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ToolReadSnapshot>> GetActiveToolsAsync(CancellationToken cancellationToken) =>
        await db.Tools.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new ToolReadSnapshot(t.Id, t.Name, t.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MaterialReadSnapshot>> GetActiveMaterialsAsync(CancellationToken cancellationToken) =>
        await db.Materials.AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.Name)
            .Select(m => new MaterialReadSnapshot(m.Id, m.Name, m.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<GeneralSupportReadSnapshot>> GetActiveGeneralSupportsAsync(CancellationToken cancellationToken) =>
        await db.GeneralSupports.AsNoTracking()
            .Where(g => g.IsActive)
            .OrderBy(g => g.Name)
            .Select(g => new GeneralSupportReadSnapshot(g.Id, g.Name, g.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CustomerReadSnapshot>> GetActiveCustomersAsync(CancellationToken cancellationToken) =>
        await db.Customers.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new CustomerReadSnapshot(c.Id, c.IataCode, c.IcaoCode, c.Name, c.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AircraftTypeReadSnapshot>> GetActiveAircraftTypesAsync(CancellationToken cancellationToken) =>
        await db.AircraftTypes.AsNoTracking()
            .Where(a => a.IsActive)
            .OrderBy(a => a.Model)
            .Select(a => new AircraftTypeReadSnapshot(a.Id, a.Manufacturer.ToString(), a.Model, a.IsActive))
            .ToListAsync(cancellationToken);
}
