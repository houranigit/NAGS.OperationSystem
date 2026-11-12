using MasterData.Contracts.Readers;

namespace Operations.Application.UnitTests;

internal sealed class SchedulingMasterDataReader(string? customerIataCode = "SV") : IMasterDataReader
{
    public bool StationExists { get; set; } = true;
    public string? StationTimeZoneId { get; set; } = "Asia/Riyadh";
    public Guid StationId { get; } = Guid.NewGuid();
    public Guid ServiceId { get; } = Guid.NewGuid();
    public IReadOnlySet<Guid>? AllowedServicesOverride { get; set; }
    public Dictionary<Guid, StaffMemberReadSnapshot> StaffMembers { get; } = [];
    public Dictionary<Guid, AircraftTypeReadSnapshot> AircraftTypes { get; } = [];
    public Dictionary<Guid, AtaChapterReadSnapshot> AtaChapters { get; } = [];
    public Dictionary<Guid, ToolReadSnapshot> Tools { get; } = [];
    public Dictionary<Guid, MaterialReadSnapshot> Materials { get; } = [];
    public Dictionary<Guid, GeneralSupportReadSnapshot> GeneralSupports { get; } = [];

    public Task<CustomerReadSnapshot?> GetCustomerAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult<CustomerReadSnapshot?>(new(id, customerIataCode, null, "Saudia", true));

    public Task<StationReadSnapshot?> GetStationAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult<StationReadSnapshot?>(StationExists ? new(id, "RUH", null, "Riyadh", true, StationTimeZoneId!) : null);

    public Task<OperationTypeReadSnapshot?> GetOperationTypeAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult<OperationTypeReadSnapshot?>(new(id, "Transit", true));

    public Task<AircraftTypeReadSnapshot?> GetAircraftTypeAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(AircraftTypes.GetValueOrDefault(id));

    public Task<ServiceReadSnapshot?> GetServiceAsync(Guid id, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<ServiceReadSnapshot>> GetServicesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ServiceReadSnapshot>>(ids.Select(id => new ServiceReadSnapshot(id, "Planned service", true)).ToList());

    public Task<StaffMemberReadSnapshot?> GetStaffMemberAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(StaffMembers.GetValueOrDefault(id));

    public Task<IReadOnlyList<StaffMemberReadSnapshot>> GetStaffMembersAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var members = ids.Where(StaffMembers.ContainsKey).Select(id => StaffMembers[id]).ToList();
        return Task.FromResult<IReadOnlyList<StaffMemberReadSnapshot>>(members);
    }

    public Task<IReadOnlyList<StaffMemberReadSnapshot>> GetActiveStaffMembersForStationAsync(Guid stationId, CancellationToken cancellationToken)
    {
        var members = StaffMembers.Values.Where(member => member.StationId == stationId && member.IsActive).ToList();
        return Task.FromResult<IReadOnlyList<StaffMemberReadSnapshot>>(members);
    }

    public Task<AtaChapterReadSnapshot?> GetAtaChapterAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(AtaChapters.GetValueOrDefault(id));
    public Task<IReadOnlyList<AtaChapterReadSnapshot>> GetActiveAtaChaptersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AtaChapterReadSnapshot>>([]);

    public Task<ToolReadSnapshot?> GetToolAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Tools.GetValueOrDefault(id));

    public Task<MaterialReadSnapshot?> GetMaterialAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Materials.GetValueOrDefault(id));

    public Task<GeneralSupportReadSnapshot?> GetGeneralSupportAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(GeneralSupports.GetValueOrDefault(id));

    public Task<ManpowerTypeReadSnapshot?> GetManpowerTypeAsync(Guid id, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IReadOnlySet<Guid>> GetAllowedActiveServiceIdsAsync(Guid manpowerTypeId, CancellationToken cancellationToken) =>
        Task.FromResult(AllowedServicesOverride ?? new HashSet<Guid> { ServiceId });

    public Task<IReadOnlyList<ServiceReadSnapshot>> GetActiveServicesAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<ToolReadSnapshot>> GetActiveToolsAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<MaterialReadSnapshot>> GetActiveMaterialsAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<GeneralSupportReadSnapshot>> GetActiveGeneralSupportsAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<CustomerReadSnapshot>> GetActiveCustomersAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<AircraftTypeReadSnapshot>> GetActiveAircraftTypesAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
