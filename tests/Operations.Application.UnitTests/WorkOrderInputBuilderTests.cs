using MasterData.Contracts.Readers;
using MasterData.Contracts.Resources;
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
                    [Guid.Empty],
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
        result.Error.Failures.Keys.ShouldContain("ServiceLines[0].PerformedByStaffMemberIds");
        result.Error.Failures.Keys.ShouldContain("Tasks[0].EmployeeIds");
        result.Error.Failures.Keys.ShouldContain("Tasks[0].Tools[0].ItemId");
    }

    [Fact]
    public async Task BuildAsync_TranslatesLegacyReturnToRampFlagsIntoOneOccurrence()
    {
        var stationId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var secondStaffId = Guid.NewGuid();
        var aircraftTypeId = Guid.NewGuid();
        var serviceLineId = Guid.NewGuid();
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
                        [staffId, secondStaffId, staffId],
                        arrival.AddMinutes(5),
                        arrival.AddMinutes(20),
                        "Return to ramp",
                        IsReturnToRamp: true,
                        Id: serviceLineId)
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
        result.Value.ServiceLines.ShouldBeEmpty();
        result.Value.Tasks.ShouldBeEmpty();
        var occurrence = result.Value.ReturnToRamps!.ShouldHaveSingleItem();
        var serviceLine = occurrence.ServiceLines.ShouldHaveSingleItem();
        serviceLine.Id.ShouldBe(serviceLineId);
        serviceLine.IsReturnToRamp.ShouldBeTrue();
        serviceLine.PerformedBy.Select(performer => performer.StaffMemberId)
            .ShouldBe([staffId, secondStaffId]);
        occurrence.Tasks.ShouldHaveSingleItem().IsReturnToRamp.ShouldBeTrue();
        occurrence.Window.From.ShouldBe(arrival.AddMinutes(5));
        occurrence.Window.To.ShouldBe(arrival.AddMinutes(20));
    }

    [Fact]
    public async Task BuildAsync_PreservesExplicitOccurrenceGroupingAndAllowsPostAtdWindows()
    {
        var stationId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var arrival = DateTimeOffset.UtcNow;
        var builder = new WorkOrderInputBuilder(new MasterDataResolver(new FakeMasterDataReader(stationId)));

        WorkOrderReturnToRampCommand Occurrence(int hour) => new(
            null,
            arrival.AddHours(hour),
            arrival.AddHours(hour).AddMinutes(30),
            $"Occurrence {hour}",
            [new WorkOrderServiceLineCommand(
                serviceId,
                [staffId],
                arrival.AddHours(hour).AddMinutes(5),
                arrival.AddHours(hour).AddMinutes(20),
                "Performed service")],
            []);

        var result = await builder.BuildAsync(
            EmptyPayload() with
            {
                ActualFlightNumber = "RJ234",
                AircraftTypeId = Guid.NewGuid(),
                ActualArrivalUtc = arrival,
                ActualDepartureUtc = arrival.AddHours(1),
                ReturnToRamps = [Occurrence(2), Occurrence(4)]
            },
            WorkOrderType.Completion,
            "RJ234",
            stationId,
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ReturnToRamps!.Count.ShouldBe(2);
        result.Value.ReturnToRamps.Select(item => item.Description)
            .ShouldBe(["Occurrence 2", "Occurrence 4"]);
    }

    [Fact]
    public async Task BuildAsync_UsesCatalogDurationTypeAndAllowsOpenEnd()
    {
        var stationId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var toolId = Guid.NewGuid();
        var arrival = DateTimeOffset.UtcNow;
        var taskFrom = arrival.AddMinutes(5);
        var taskTo = arrival.AddMinutes(45);
        var builder = new WorkOrderInputBuilder(new MasterDataResolver(new FakeMasterDataReader(stationId)));

        var result = await builder.BuildAsync(
            CompletionPayload(arrival, new WorkOrderTaskCommand(
                null,
                TaskType.Major,
                null,
                taskFrom,
                taskTo,
                [staffId],
                [new WorkOrderTaskToolCommand(toolId, null, taskFrom.AddMinutes(2), null)],
                [],
                [])),
            WorkOrderType.Completion,
            "RJ234",
            stationId,
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var tool = result.Value.Tasks.ShouldHaveSingleItem().Tools.ShouldHaveSingleItem();
        tool.Tool.CalculationType.ShouldBe(ResourceCalculationType.Duration);
        tool.Usage.Quantity.ShouldBeNull();
        tool.Usage.FromUtc.ShouldBe(taskFrom.AddMinutes(2));
        tool.Usage.ToUtc.ShouldBeNull();
    }

    [Fact]
    public async Task BuildAsync_TranslatesLegacyQuantityOnlyDurationResourceToTaskWindow()
    {
        var stationId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var arrival = DateTimeOffset.UtcNow;
        var taskFrom = arrival.AddMinutes(5);
        var taskTo = arrival.AddMinutes(45);
        var builder = new WorkOrderInputBuilder(new MasterDataResolver(new FakeMasterDataReader(stationId)));

        var result = await builder.BuildAsync(
            CompletionPayload(arrival, new WorkOrderTaskCommand(
                null,
                TaskType.Minor,
                null,
                taskFrom,
                taskTo,
                [staffId],
                [new WorkOrderTaskToolCommand(Guid.NewGuid(), 1)],
                [],
                [])),
            WorkOrderType.Completion,
            "RJ234",
            stationId,
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var usage = result.Value.Tasks.ShouldHaveSingleItem().Tools.ShouldHaveSingleItem().Usage;
        usage.Quantity.ShouldBeNull();
        usage.FromUtc.ShouldBe(taskFrom);
        usage.ToUtc.ShouldBe(taskTo);
    }

    [Fact]
    public async Task BuildAsync_RejectsDuplicateResourceRowsAndDurationOutsideTask()
    {
        var stationId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var toolId = Guid.NewGuid();
        var arrival = DateTimeOffset.UtcNow;
        var taskFrom = arrival.AddMinutes(5);
        var taskTo = arrival.AddMinutes(45);
        var builder = new WorkOrderInputBuilder(new MasterDataResolver(new FakeMasterDataReader(stationId)));

        var duplicate = await builder.BuildAsync(
            CompletionPayload(arrival, new WorkOrderTaskCommand(
                null,
                TaskType.Major,
                null,
                taskFrom,
                taskTo,
                [staffId],
                [new WorkOrderTaskToolCommand(toolId, 1), new WorkOrderTaskToolCommand(toolId, 1)],
                [],
                [])),
            WorkOrderType.Completion,
            "RJ234",
            stationId,
            CancellationToken.None);

        duplicate.IsFailure.ShouldBeTrue();
        duplicate.Error.Failures!.Keys.ShouldContain("Tasks[0].Tools[1].ItemId");

        var outsideTask = await builder.BuildAsync(
            CompletionPayload(arrival, new WorkOrderTaskCommand(
                null,
                TaskType.Major,
                null,
                taskFrom,
                taskTo,
                [staffId],
                [new WorkOrderTaskToolCommand(toolId, null, taskFrom.AddMinutes(-1), null)],
                [],
                [])),
            WorkOrderType.Completion,
            "RJ234",
            stationId,
            CancellationToken.None);

        outsideTask.IsFailure.ShouldBeTrue();
        outsideTask.Error.Code.ShouldBe("Operations.ResourceUsage.FromBeforeTask");
    }

    [Fact]
    public async Task BuildAsync_AppliesDuplicateAndDurationBoundsToReturnToRampTaskResources()
    {
        var stationId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var toolId = Guid.NewGuid();
        var arrival = DateTimeOffset.UtcNow;
        var occurrenceFrom = arrival.AddHours(2);
        var occurrenceTo = occurrenceFrom.AddHours(1);
        var taskFrom = occurrenceFrom.AddMinutes(5);
        var taskTo = occurrenceTo.AddMinutes(-5);
        var builder = new WorkOrderInputBuilder(new MasterDataResolver(new FakeMasterDataReader(stationId)));

        WorkOrderEditableCommandPayload PayloadWith(params WorkOrderTaskToolCommand[] tools) =>
            EmptyPayload() with
            {
                ActualFlightNumber = "RJ234",
                AircraftTypeId = Guid.NewGuid(),
                ActualArrivalUtc = arrival,
                ActualDepartureUtc = arrival.AddHours(1),
                ReturnToRamps =
                [
                    new WorkOrderReturnToRampCommand(
                        null,
                        occurrenceFrom,
                        occurrenceTo,
                        null,
                        [],
                        [new WorkOrderTaskCommand(
                            null,
                            TaskType.Minor,
                            "RTR resource validation",
                            taskFrom,
                            taskTo,
                            [staffId],
                            tools,
                            [],
                            [])])
                ]
            };

        var duplicate = await builder.BuildAsync(
            PayloadWith(
                new WorkOrderTaskToolCommand(toolId, 1),
                new WorkOrderTaskToolCommand(toolId, 1)),
            WorkOrderType.Completion,
            "RJ234",
            stationId,
            CancellationToken.None);

        duplicate.IsFailure.ShouldBeTrue();
        duplicate.Error.Failures!.Keys.ShouldContain("ReturnToRamps[0].Tasks[0].Tools[1].ItemId");

        var outsideTask = await builder.BuildAsync(
            PayloadWith(new WorkOrderTaskToolCommand(toolId, null, taskFrom, taskTo.AddMinutes(1))),
            WorkOrderType.Completion,
            "RJ234",
            stationId,
            CancellationToken.None);

        outsideTask.IsFailure.ShouldBeTrue();
        outsideTask.Error.Code.ShouldBe("Operations.ResourceUsage.ToAfterTask");
    }

    private static WorkOrderEditableCommandPayload CompletionPayload(
        DateTimeOffset arrival,
        WorkOrderTaskCommand task) =>
        EmptyPayload() with
        {
            ActualFlightNumber = "RJ234",
            AircraftTypeId = Guid.NewGuid(),
            ActualArrivalUtc = arrival,
            ActualDepartureUtc = arrival.AddHours(1),
            Tasks = [task]
        };

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
            Task.FromResult<ToolReadSnapshot?>(new(id, "Towbar", IsActive: true, CalculationType: ResourceCalculationType.Duration));

        public Task<MaterialReadSnapshot?> GetMaterialAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<MaterialReadSnapshot?>(new(id, "Hydraulic fluid", IsActive: true, CalculationType: ResourceCalculationType.Quantity));

        public Task<GeneralSupportReadSnapshot?> GetGeneralSupportAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<GeneralSupportReadSnapshot?>(new(id, "GPU", IsActive: true, CalculationType: ResourceCalculationType.Quantity));

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
