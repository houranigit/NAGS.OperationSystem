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
    public void Request_serializes_pending_attachments()
    {
        var request = new WorkOrderServiceLineRequestModel(
            Guid.NewGuid(),
            [Guid.NewGuid()],
            DateTimeOffset.Parse("2026-07-20T10:00:00Z"),
            DateTimeOffset.Parse("2026-07-20T11:00:00Z"),
            "Photo evidence",
            Attachments:
            [
                new WorkOrderServiceLineAttachmentRequestModel(
                    "Image",
                    Convert.ToBase64String([1, 2, 3]),
                    "service.jpg",
                    "image/jpeg")
            ]);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(request, JsonOptions));
        var attachment = json.RootElement.GetProperty("attachments")[0];

        attachment.GetProperty("kind").GetString().ShouldBe("Image");
        attachment.GetProperty("base64Content").GetString().ShouldBe("AQID");
        attachment.GetProperty("fileName").GetString().ShouldBe("service.jpg");
        attachment.GetProperty("contentType").GetString().ShouldBe("image/jpeg");
    }

    [Fact]
    public void Response_deserializes_multiple_performers_and_attachments()
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
              "attachments": [
                {
                  "id": "{{Guid.NewGuid()}}",
                  "kind": "Document",
                  "originalFileName": "service-report.pdf",
                  "contentType": "application/pdf",
                  "size": 2048
                }
              ],
              "isReturnToRamp": false
            }
            """;

        var line = JsonSerializer.Deserialize<WorkOrderServiceLineModel>(payload, JsonOptions);

        line.ShouldNotBeNull();
        line.PerformedBy.Select(performer => performer.StaffMemberId).ShouldBe([firstId, secondId]);
        line.Attachments.ShouldNotBeNull();
        line.Attachments!.Count.ShouldBe(1);
        line.Attachments[0].OriginalFileName.ShouldBe("service-report.pdf");
        line.Attachments[0].Size.ShouldBe(2048);
    }
}
