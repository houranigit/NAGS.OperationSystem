using MasterData.Contracts.Readers;
using Operations.Application.Common;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class MasterDataResolverTests
{
    [Fact]
    public async Task StaffMembersForStationAsync_RejectsStaffFromAnotherStation()
    {
        var selectedStationId = Guid.NewGuid();
        var otherStationId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var reader = new FakeMasterDataReader
        {
            StaffMembers =
            {
                [staffId] = new StaffMemberReadSnapshot(staffId, "Abdalla Ahmed", "50001", otherStationId, Guid.NewGuid(), true)
            }
        };
        var resolver = new MasterDataResolver(reader);

        var result = await resolver.StaffMembersForStationAsync([staffId], selectedStationId, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.Ref.StaffStationMismatch");
    }

    [Fact]
    public async Task StaffMembersForStationAsync_ReturnsSnapshotsForSelectedStation()
    {
        var stationId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var reader = new FakeMasterDataReader
        {
            StaffMembers =
            {
                [staffId] = new StaffMemberReadSnapshot(staffId, "Abdalla Ahmed", "50001", stationId, Guid.NewGuid(), true)
            }
        };
        var resolver = new MasterDataResolver(reader);

        var result = await resolver.StaffMembersForStationAsync([staffId], stationId, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Single().StaffMemberId.ShouldBe(staffId);
    }

    [Fact]
    public async Task EnsurePerformedServicesAllowedAsync_RejectsServicesOutsideTheStaffManpowerType()
    {
        var manpowerTypeId = Guid.NewGuid();
        var allowedServiceId = Guid.NewGuid();
        var disallowedServiceId = Guid.NewGuid();
        var reader = new FakeMasterDataReader();
        reader.AllowedServiceIds.Add(allowedServiceId);
        var resolver = new MasterDataResolver(reader);

        var result = await resolver.EnsurePerformedServicesAllowedAsync(
            [allowedServiceId, disallowedServiceId],
            manpowerTypeId,
            isAdministrator: false,
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.WorkOrder.ServiceNotAllowed");
    }

    [Fact]
    public async Task EnsurePerformedServicesAllowedAsync_AllowsMappedServicesAndAdministratorBypass()
    {
        var manpowerTypeId = Guid.NewGuid();
        var allowedServiceId = Guid.NewGuid();
        var reader = new FakeMasterDataReader();
        reader.AllowedServiceIds.Add(allowedServiceId);
        var resolver = new MasterDataResolver(reader);

        (await resolver.EnsurePerformedServicesAllowedAsync(
            [allowedServiceId], manpowerTypeId, isAdministrator: false, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await resolver.EnsurePerformedServicesAllowedAsync(
            [Guid.NewGuid()], manpowerTypeId: null, isAdministrator: true, CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    private sealed class FakeMasterDataReader : IMasterDataReader
    {
        public Dictionary<Guid, StaffMemberReadSnapshot> StaffMembers { get; } = [];
        public HashSet<Guid> AllowedServiceIds { get; } = [];

        public Task<CustomerReadSnapshot?> GetCustomerAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<StationReadSnapshot?> GetStationAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<OperationTypeReadSnapshot?> GetOperationTypeAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<AircraftTypeReadSnapshot?> GetAircraftTypeAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<ServiceReadSnapshot?> GetServiceAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<ServiceReadSnapshot>> GetServicesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

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

        public Task<ToolReadSnapshot?> GetToolAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<MaterialReadSnapshot?> GetMaterialAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<GeneralSupportReadSnapshot?> GetGeneralSupportAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<ManpowerTypeReadSnapshot?> GetManpowerTypeAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlySet<Guid>> GetAllowedActiveServiceIdsAsync(Guid manpowerTypeId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(AllowedServiceIds);

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
}
