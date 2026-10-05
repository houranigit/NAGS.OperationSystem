using MasterData.Contracts.Readers;
using MasterData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Infrastructure.Readers;

public sealed class LegacySystemIdReader(MasterDataDbContext db) : ILegacySystemIdReader
{
    public async Task<LegacySystemIdLookup> GetAsync(LegacySystemIdRequest request, CancellationToken cancellationToken)
    {
        var staffIds = request.StaffMembers?.Distinct().ToArray() ?? [];
        var staffRows = staffIds.Length == 0
            ? []
            : await db.StaffMembers.AsNoTracking()
                .Where(x => staffIds.Contains(x.Id))
                .Select(x => new { x.Id, x.LegacySystemId, x.ManpowerTypeId })
                .ToListAsync(cancellationToken);
        var staffMembers = staffRows.ToDictionary(x => x.Id, x => x.LegacySystemId);
        var staffMemberManpowerTypeIds = staffRows.ToDictionary(x => x.Id, x => x.ManpowerTypeId);
        var manpowerTypeIds = (request.ManpowerTypes ?? [])
            .Concat(staffMemberManpowerTypeIds.Values).Distinct().ToArray();
        var servicesIds = request.Services?.Distinct().ToArray() ?? [];
        IReadOnlyDictionary<Guid, string?> services = servicesIds.Length == 0
            ? new Dictionary<Guid, string?>()
            : await db.Services.AsNoTracking()
                .Where(x => servicesIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.LegacySystemId, cancellationToken);
        var toolsIds = request.Tools?.Distinct().ToArray() ?? [];
        IReadOnlyDictionary<Guid, string?> tools = toolsIds.Length == 0
            ? new Dictionary<Guid, string?>()
            : await db.Tools.AsNoTracking()
                .Where(x => toolsIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.LegacySystemId, cancellationToken);
        var materialsIds = request.Materials?.Distinct().ToArray() ?? [];
        IReadOnlyDictionary<Guid, string?> materials = materialsIds.Length == 0
            ? new Dictionary<Guid, string?>()
            : await db.Materials.AsNoTracking()
                .Where(x => materialsIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.LegacySystemId, cancellationToken);
        var generalSupportsIds = request.GeneralSupports?.Distinct().ToArray() ?? [];
        IReadOnlyDictionary<Guid, string?> generalSupports = generalSupportsIds.Length == 0
            ? new Dictionary<Guid, string?>()
            : await db.GeneralSupports.AsNoTracking()
                .Where(x => generalSupportsIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.LegacySystemId, cancellationToken);
        var customersIds = request.Customers?.Distinct().ToArray() ?? [];
        IReadOnlyDictionary<Guid, string?> customers = customersIds.Length == 0
            ? new Dictionary<Guid, string?>()
            : await db.Customers.AsNoTracking()
                .Where(x => customersIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.LegacySystemId, cancellationToken);
        var stationsIds = request.Stations?.Distinct().ToArray() ?? [];
        IReadOnlyDictionary<Guid, string?> stations = stationsIds.Length == 0
            ? new Dictionary<Guid, string?>()
            : await db.Stations.AsNoTracking()
                .Where(x => stationsIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.LegacySystemId, cancellationToken);
        IReadOnlyDictionary<Guid, string?> manpowerTypes = manpowerTypeIds.Length == 0
            ? new Dictionary<Guid, string?>()
            : await db.ManpowerTypes.AsNoTracking()
                .Where(x => manpowerTypeIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.LegacySystemId, cancellationToken);
        var operationTypesIds = request.OperationTypes?.Distinct().ToArray() ?? [];
        IReadOnlyDictionary<Guid, string?> operationTypes = operationTypesIds.Length == 0
            ? new Dictionary<Guid, string?>()
            : await db.OperationTypes.AsNoTracking()
                .Where(x => operationTypesIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.LegacySystemId, cancellationToken);
        var aircraftTypesIds = request.AircraftTypes?.Distinct().ToArray() ?? [];
        IReadOnlyDictionary<Guid, string?> aircraftTypes = aircraftTypesIds.Length == 0
            ? new Dictionary<Guid, string?>()
            : await db.AircraftTypes.AsNoTracking()
                .Where(x => aircraftTypesIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.LegacySystemId, cancellationToken);

        return new LegacySystemIdLookup(
            services, tools, materials, generalSupports, customers, stations, manpowerTypes,
            operationTypes, aircraftTypes, staffMembers, staffMemberManpowerTypeIds);
    }
}
