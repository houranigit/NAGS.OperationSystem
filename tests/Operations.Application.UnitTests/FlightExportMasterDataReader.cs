using MasterData.Contracts.Readers;

namespace Operations.Application.UnitTests;

internal sealed class FlightExportMasterDataReader(
    IReadOnlyList<StaffMemberReadSnapshot>? staff = null,
    IReadOnlyList<ManpowerTypeReadSnapshot>? manpowerTypes = null) : IMasterDataReader
{
    private readonly IReadOnlyDictionary<Guid, StaffMemberReadSnapshot> staffById =
        (staff ?? []).ToDictionary(item => item.Id);
    private readonly IReadOnlyDictionary<Guid, ManpowerTypeReadSnapshot> manpowerTypesById =
        (manpowerTypes ?? []).ToDictionary(item => item.Id);

    public List<Guid> RequestedStaffIds { get; } = [];
    public List<Guid> RequestedManpowerTypeIds { get; } = [];

    public Task<IReadOnlyList<StaffMemberReadSnapshot>> GetStaffMembersAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        RequestedStaffIds.AddRange(ids);
        IReadOnlyList<StaffMemberReadSnapshot> result = ids
            .Where(staffById.ContainsKey)
            .Select(id => staffById[id])
            .ToList();
        return Task.FromResult(result);
    }

    public Task<ManpowerTypeReadSnapshot?> GetManpowerTypeAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        RequestedManpowerTypeIds.Add(id);
        return Task.FromResult(manpowerTypesById.GetValueOrDefault(id));
    }

    public Task<CustomerReadSnapshot?> GetCustomerAsync(Guid id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<StationReadSnapshot?> GetStationAsync(Guid id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<OperationTypeReadSnapshot?> GetOperationTypeAsync(Guid id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<AircraftTypeReadSnapshot?> GetAircraftTypeAsync(Guid id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<ServiceReadSnapshot?> GetServiceAsync(Guid id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<IReadOnlyList<ServiceReadSnapshot>> GetServicesAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<StaffMemberReadSnapshot?> GetStaffMemberAsync(Guid id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<IReadOnlyList<StaffMemberReadSnapshot>> GetActiveStaffMembersForStationAsync(
        Guid stationId,
        CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<AtaChapterReadSnapshot?> GetAtaChapterAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult<AtaChapterReadSnapshot?>(null);
    public Task<IReadOnlyList<AtaChapterReadSnapshot>> GetActiveAtaChaptersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AtaChapterReadSnapshot>>([]);

    public Task<ToolReadSnapshot?> GetToolAsync(Guid id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<MaterialReadSnapshot?> GetMaterialAsync(Guid id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<GeneralSupportReadSnapshot?> GetGeneralSupportAsync(Guid id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<IReadOnlySet<Guid>> GetAllowedActiveServiceIdsAsync(
        Guid manpowerTypeId,
        CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyList<ServiceReadSnapshot>> GetActiveServicesAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<IReadOnlyList<ToolReadSnapshot>> GetActiveToolsAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<IReadOnlyList<MaterialReadSnapshot>> GetActiveMaterialsAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<IReadOnlyList<GeneralSupportReadSnapshot>> GetActiveGeneralSupportsAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<IReadOnlyList<CustomerReadSnapshot>> GetActiveCustomersAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<IReadOnlyList<AircraftTypeReadSnapshot>> GetActiveAircraftTypesAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
