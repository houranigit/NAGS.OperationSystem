using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Features.Operations.Components;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Operations;

public sealed class WorkOrderMergeMappingTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Canonical_merge_mapping_excludes_flattened_children_and_preserves_occurrences()
    {
        var first = Source("First", 0, 60);
        var second = Source("Second", 120);

        first.ServiceLines.Where(WorkOrderMergeMapping.IsStandardServiceLine)
            .Select(line => line.ServiceName)
            .ShouldBe(["First Standard Service"]);
        first.Tasks.Where(WorkOrderMergeMapping.IsStandardTask)
            .Select(task => task.Description)
            .ShouldBe(["First Standard Task"]);

        var mapped = WorkOrderMergeMapping.BuildCanonicalReturnToRamps([first, second]);

        mapped.ShouldNotBeNull();
        mapped!.Select(item => item.Description)
            .ShouldBe(["First RTR 0", "First RTR 60", "Second RTR 120"]);
        mapped.SelectMany(item => item.ServiceLines).Count().ShouldBe(3);
        mapped.SelectMany(item => item.Tasks).Count().ShouldBe(3);
        mapped.ShouldAllBe(item => item.Id == null);
        mapped.SelectMany(item => item.ServiceLines)
            .ShouldAllBe(line => line.Id == null && !line.IsReturnToRamp);
        mapped.SelectMany(item => item.Tasks)
            .ShouldAllBe(task => task.Id == null && !task.IsReturnToRamp);
    }

    [Fact]
    public void Missing_canonical_collection_defers_to_the_backend_cloner()
    {
        var legacyResponse = Source("Legacy", 0) with { ReturnToRamps = null };

        WorkOrderMergeMapping.BuildCanonicalReturnToRamps([legacyResponse])
            .ShouldBeNull();
    }

    private static WorkOrderDetail Source(string prefix, params int[] occurrenceOffsets)
    {
        var standardService = Service($"{prefix} Standard Service", Now, isReturnToRamp: false);
        var standardTask = Task($"{prefix} Standard Task", Now, isReturnToRamp: false);
        var occurrences = occurrenceOffsets
            .Select(offset => Occurrence(prefix, offset))
            .ToList();

        return new WorkOrderDetail(
            Id: Guid.NewGuid(),
            FlightId: Guid.NewGuid(),
            Type: "Completion",
            Status: "Submitted",
            IsMergeGenerated: false,
            MergedIntoWorkOrderId: null,
            OwnerUserId: Guid.NewGuid(),
            OwnerName: prefix,
            CustomerId: Guid.NewGuid(),
            CustomerIataCode: "SV",
            CustomerName: "Saudia",
            StationId: Guid.NewGuid(),
            StationIata: "RUH",
            StationName: "Riyadh",
            OperationTypeId: Guid.NewGuid(),
            OperationTypeName: "Transit",
            PlannedFlightNumber: "SV101",
            ScheduledArrivalUtc: Now,
            ScheduledDepartureUtc: Now.AddHours(1),
            ActualFlightNumber: "SV101",
            AircraftTypeId: null,
            AircraftTypeModel: null,
            AircraftTailNumber: null,
            ActualArrivalUtc: Now,
            ActualDepartureUtc: Now.AddHours(1),
            CanceledAtUtc: null,
            CancellationReason: null,
            Remarks: null,
            CustomerSignature: null,
            ApprovalSequence: null,
            ApprovalNumber: null,
            ApprovedByUserId: null,
            ApprovedAtUtc: null,
            ServiceLines: [standardService, .. occurrences.SelectMany(item => item.ServiceLines)],
            Tasks: [standardTask, .. occurrences.SelectMany(item => item.Tasks)],
            CreatedAtUtc: Now,
            UpdatedAtUtc: null,
            RowVersion: "AQID",
            ReturnToRamps: occurrences);
    }

    private static WorkOrderReturnToRampModel Occurrence(string prefix, int offset)
    {
        var from = Now.AddMinutes(offset);
        return new WorkOrderReturnToRampModel(
            Guid.NewGuid(),
            from,
            from.AddMinutes(30),
            $"{prefix} RTR {offset}",
            Guid.NewGuid(),
            from,
            [Service($"{prefix} RTR Service {offset}", from, isReturnToRamp: true)],
            [Task($"{prefix} RTR Task {offset}", from, isReturnToRamp: true)]);
    }

    private static WorkOrderServiceLineModel Service(
        string name,
        DateTimeOffset from,
        bool isReturnToRamp) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            name,
            [new WorkOrderServiceLinePerformerModel(Guid.NewGuid(), "Agent", "E-1")],
            from,
            from.AddMinutes(20),
            null,
            isReturnToRamp);

    private static WorkOrderTaskModel Task(
        string description,
        DateTimeOffset from,
        bool isReturnToRamp) =>
        new(
            Guid.NewGuid(),
            "Minor",
            description,
            from,
            from.AddMinutes(20),
            [new WorkOrderTaskEmployeeModel(Guid.NewGuid(), "Agent", "E-1")],
            [],
            [],
            [],
            [],
            isReturnToRamp);
}
