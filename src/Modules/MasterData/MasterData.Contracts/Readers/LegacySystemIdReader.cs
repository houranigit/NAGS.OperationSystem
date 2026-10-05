namespace MasterData.Contracts.Readers;

/// <summary>Existing, scope-checked references for the legacy export only; null collections are empty.</summary>
public sealed record LegacySystemIdRequest(
    IReadOnlyCollection<Guid>? Services = null,
    IReadOnlyCollection<Guid>? Tools = null,
    IReadOnlyCollection<Guid>? Materials = null,
    IReadOnlyCollection<Guid>? GeneralSupports = null,
    IReadOnlyCollection<Guid>? Customers = null,
    IReadOnlyCollection<Guid>? Stations = null,
    IReadOnlyCollection<Guid>? ManpowerTypes = null,
    IReadOnlyCollection<Guid>? OperationTypes = null,
    IReadOnlyCollection<Guid>? AircraftTypes = null,
    IReadOnlyCollection<Guid>? StaffMembers = null);

public sealed record LegacySystemIdLookup(
    IReadOnlyDictionary<Guid, string?> Services,
    IReadOnlyDictionary<Guid, string?> Tools,
    IReadOnlyDictionary<Guid, string?> Materials,
    IReadOnlyDictionary<Guid, string?> GeneralSupports,
    IReadOnlyDictionary<Guid, string?> Customers,
    IReadOnlyDictionary<Guid, string?> Stations,
    IReadOnlyDictionary<Guid, string?> ManpowerTypes,
    IReadOnlyDictionary<Guid, string?> OperationTypes,
    IReadOnlyDictionary<Guid, string?> AircraftTypes,
    IReadOnlyDictionary<Guid, string?> StaffMembers,
    IReadOnlyDictionary<Guid, Guid> StaffMemberManpowerTypeIds);

/// <summary>
/// Reads current legacy mappings separately from operational snapshots and mobile catalog contracts.
/// Callers must first authorize and scope the records they export.
/// </summary>
public interface ILegacySystemIdReader
{
    public Task<LegacySystemIdLookup> GetAsync(LegacySystemIdRequest request, CancellationToken cancellationToken);
}
