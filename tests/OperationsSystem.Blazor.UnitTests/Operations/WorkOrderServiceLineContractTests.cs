using System.Text.Json;
using OperationsSystem.Blazor.Client.Api;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Operations;

public sealed class WorkOrderServiceLineContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Request_serializes_every_selected_performer()
    {
        var performerIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var request = new WorkOrderServiceLineRequestModel(
            Guid.NewGuid(),
            performerIds,
            DateTimeOffset.Parse("2026-07-20T10:00:00Z"),
            DateTimeOffset.Parse("2026-07-20T11:00:00Z"),
            "Handled together");

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(request, JsonOptions));

        json.RootElement.GetProperty("performedByStaffMemberIds")
            .EnumerateArray()
            .Select(value => value.GetGuid())
            .ShouldBe(performerIds);
    }

    [Fact]
    public void Response_deserializes_multiple_performers()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var payload = $$"""
            {
              "id": "{{lineId}}",
              "serviceId": "{{serviceId}}",
              "serviceName": "Deicing",
              "performedBy": [
                { "staffMemberId": "{{firstId}}", "fullName": "First Employee", "employeeId": "E-1" },
                { "staffMemberId": "{{secondId}}", "fullName": "Second Employee", "employeeId": "E-2" }
              ],
              "fromUtc": "2026-07-20T10:00:00Z",
              "toUtc": "2026-07-20T11:00:00Z",
              "description": null,
              "isReturnToRamp": false
            }
            """;

        var line = JsonSerializer.Deserialize<WorkOrderServiceLineModel>(payload, JsonOptions);

        line.ShouldNotBeNull();
        line.PerformedBy.Select(performer => performer.StaffMemberId).ShouldBe([firstId, secondId]);
    }
}
