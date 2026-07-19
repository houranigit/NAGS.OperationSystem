using MasterData.Contracts.Readers;
using Operations.Application.Common;
using Operations.Application.Features.WorkOrders;
using Operations.Domain.Enumerations;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class WorkOrderInputBuilderTests
{
    [Fact]
    public async Task BuildAsync_AllowsCompletionWithoutServiceLinesOrTasks()
    {
        var aircraftTypeId = Guid.NewGuid();
        var builder = new WorkOrderInputBuilder(new MasterDataResolver(new FakeMasterDataReader()));
        var arrival = DateTimeOffset.UtcNow;

        var result = await builder.BuildAsync(
            EmptyPayload() with
            {
                ActualFlightNumber = "RJ234",
                AircraftTypeId = aircraftTypeId,
                ActualArrivalUtc = arrival,
                ActualDepartureUtc = arrival.AddHours(1)
            },
            WorkOrderType.Completion,
            "RJ234",
            Guid.NewGuid(),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ServiceLines.ShouldBeEmpty();
        result.Value.Tasks.ShouldBeEmpty();
    }

    [Fact]
    public async Task BuildAsync_RejectsCancellationWithoutRequiredDetails()
    {
        var builder = new WorkOrderInputBuilder(new MasterDataResolver(new FakeMasterDataReader()));

        var result = await builder.BuildAsync(
            EmptyPayload(),
            WorkOrderType.Cancellation,
            "RJ234",
            Guid.NewGuid(),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.WorkOrder.Validation");
        result.Error.Failures!.Keys.ShouldContain(nameof(WorkOrderEditableCommandPayload.CanceledAtUtc));
        result.Error.Failures.Keys.ShouldContain(nameof(WorkOrderEditableCommandPayload.CancellationReason));
    }

    [Fact]
    public async Task BuildAsync_RejectsIncompleteCompletionRows()
    {
        var builder = new WorkOrderInputBuilder(new MasterDataResolver(new FakeMasterDataReader()));

        var payload = EmptyPayload() with
        {
            ActualArrivalUtc = DateTimeOffset.UtcNow,
            ServiceLines =
            [
                new WorkOrderServiceLineCommand(
                    Guid.Empty,
                    Guid.Empty,
                    default,
                    default,
                    Description: null)
            ],
            Tasks =
            [
                new WorkOrderTaskCommand(
                    Id: null,
                    TaskType.Major,
                    Description: null,
                    FromUtc: default,
                    ToUtc: default,
                    EmployeeIds: [],
                    Tools: [new WorkOrderTaskToolCommand(Guid.Empty, 0)],
                    Materials: [],
                    GeneralSupports: [])
            ]
        };

        var result = await builder.BuildAsync(
            payload,
            WorkOrderType.Completion,
            "RJ234",
            Guid.NewGuid(),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.WorkOrder.Validation");
        result.Error.Failures!.Keys.ShouldContain(nameof(WorkOrderEditableCommandPayload.ActualFlightNumber));
        result.Error.Failures.Keys.ShouldContain(nameof(WorkOrderEditableCommandPayload.AircraftTypeId));
        result.Error.Failures!.Keys.ShouldContain(nameof(WorkOrderEditableCommandPayload.ActualArrivalUtc));
        result.Error.Failures.Keys.ShouldContain(nameof(WorkOrderEditableCommandPayload.ActualDepartureUtc));
        result.Error.Failures.Keys.ShouldContain("ServiceLines[0].ServiceId");
        result.Error.Failures.Keys.ShouldContain("Tasks[0].EmployeeIds");
        result.Error.Failures.Keys.ShouldContain("Tasks[0].Tools[0].ItemId");
    }

    [Fact]
    public async Task BuildAsync_CopiesReturnToRampProvenanceToDomainInput()
    {
        var stationId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var aircraftTypeId = Guid.NewGuid();
        var arrival = DateTimeOffset.UtcNow;
        var reader = new FakeMasterDataReader(stationId);
        var builder = new WorkOrderInputBuilder(new MasterDataResolver(reader));

        var result = await builder.BuildAsync(
            EmptyPayload() with
            {
                ActualFlightNumber = "RJ234",
                AircraftTypeId = aircraftTypeId,
                ActualArrivalUtc = arrival,
                ActualDepartureUtc = arrival.AddHours(1),
                ServiceLines =
                [
                    new WorkOrderServiceLineCommand(
                        serviceId,
                        staffId,
                        arrival.AddMinutes(5),
                        arrival.AddMinutes(20),
                        "Return to ramp",
                        IsReturnToRamp: true)
                ],
                Tasks =
                [
                    new WorkOrderTaskCommand(
                        Id: null,
                        TaskType.Minor,
                        "Ramp inspection",
                        arrival.AddMinutes(5),
                        arrival.AddMinutes(20),
                        EmployeeIds: [staffId],
                        Tools: [],
                        Materials: [],
                        GeneralSupports: [],
                        Attachments: null,
                        IsReturnToRamp: true)
                ]
            },
            WorkOrderType.Completion,
            "RJ234",
            stationId,
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ServiceLines.ShouldHaveSingleItem().IsReturnToRamp.ShouldBeTrue();
        result.Value.Tasks.ShouldHaveSingleItem().IsReturnToRamp.ShouldBeTrue();
    }

    private static WorkOrderEditableCommandPayload EmptyPayload() =>
        new(
            ActualFlightNumber: null,
            AircraftTypeId: null,
            AircraftTailNumber: null,
            ActualArrivalUtc: null,
            ActualDepartureUtc: null,
            CanceledAtUtc: null,
            CancellationReason: null,
            Remarks: null,
            ServiceLines: [],
            Tasks: []);

    private sealed class FakeMasterDataReader(Guid? stationId = null) : IMasterDataReader
    {
        public Task<CustomerReadSnapshot?> GetCustomerAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<StationReadSnapshot?> GetStationAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<OperationTypeReadSnapshot?> GetOperationTypeAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<AircraftTypeReadSnapshot?> GetAircraftTypeAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<AircraftTypeReadSnapshot?>(new(id, "Airbus", "A320", IsActive: true));

        public Task<ServiceReadSnapshot?> GetServiceAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<ServiceReadSnapshot?>(new(id, "Marshalling", IsActive: true));

        public Task<IReadOnlyList<ServiceReadSnapshot>> GetServicesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<StaffMemberReadSnapshot?> GetStaffMemberAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<StaffMemberReadSnapshot>> GetStaffMembersAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StaffMemberReadSnapshot>>(ids
                .Select(id => new StaffMemberReadSnapshot(
                    id,
                    "Ramp Agent",
                    "EMP-1",
                    stationId ?? Guid.Empty,
                    Guid.NewGuid(),
                    IsActive: true))
                .ToList());

        public Task<IReadOnlyList<StaffMemberReadSnapshot>> GetActiveStaffMembersForStationAsync(Guid stationId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<ToolReadSnapshot?> GetToolAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<MaterialReadSnapshot?> GetMaterialAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<GeneralSupportReadSnapshot?> GetGeneralSupportAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<ManpowerTypeReadSnapshot?> GetManpowerTypeAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlySet<Guid>> GetAllowedActiveServiceIdsAsync(Guid manpowerTypeId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());

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
