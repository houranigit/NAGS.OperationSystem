using System.Text.Json;
using Operations.Api.Endpoints;
using Operations.Application.Contracts;
using Operations.Domain.Enumerations;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed class OperationsRequestMappingTests
{
    [Fact]
    public void WorkOrderRequest_MapsLegacySingularServicePerformerIntoCurrentCollection()
    {
        var performerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var request = new WorkOrderRequest(
            WorkOrderType.Completion,
            ActualFlightNumber: "MOB100",
            AircraftTypeId: null,
            AircraftTailNumber: null,
            ActualArrivalUtc: now,
            ActualDepartureUtc: now.AddHours(1),
            CanceledAtUtc: null,
            CancellationReason: null,
            Remarks: null,
            ServiceLines:
            [
                new WorkOrderServiceLineRequest(
                    Guid.NewGuid(),
                    PerformedByStaffMemberIds: null,
                    now,
                    now.AddMinutes(30),
                    Description: null,
                    PerformedByStaffMemberId: performerId)
            ],
            Tasks: null);

        var line = request.ToPayload().ServiceLines.ShouldHaveSingleItem();

        line.PerformedByStaffMemberIds.ShouldBe([performerId]);
    }

    [Fact]
    public void WorkOrderRequest_PrefersCurrentPerformerCollectionOverLegacyAlias()
    {
        var currentPerformerIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var now = DateTimeOffset.UtcNow;
        var request = new WorkOrderServiceLineRequest(
            Guid.NewGuid(),
            currentPerformerIds,
            now,
            now.AddMinutes(30),
            Description: null,
            PerformedByStaffMemberId: Guid.NewGuid());

        request.ResolvePerformedByStaffMemberIds().ShouldBe(currentPerformerIds);
    }

    [Fact]
    public void WorkOrderServiceLineDto_SerializesCurrentCollectionAndLegacyFirstPerformerAliases()
    {
        var first = new WorkOrderServiceLinePerformerDto(Guid.NewGuid(), "First Agent", "EMP-1");
        var second = new WorkOrderServiceLinePerformerDto(Guid.NewGuid(), "Second Agent", "EMP-2");
        var now = DateTimeOffset.UtcNow;
        var dto = new WorkOrderServiceLineDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Marshalling",
            [first, second],
            now,
            now.AddMinutes(30),
            Description: null,
            IsReturnToRamp: false);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            dto,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var root = json.RootElement;

        root.GetProperty("performedBy").GetArrayLength().ShouldBe(2);
        root.GetProperty("performedByStaffMemberId").GetGuid().ShouldBe(first.StaffMemberId);
        root.GetProperty("performedByName").GetString().ShouldBe(first.FullName);
    }
}
