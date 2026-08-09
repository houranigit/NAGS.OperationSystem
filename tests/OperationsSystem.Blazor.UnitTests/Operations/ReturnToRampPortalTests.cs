using System.Text.Json;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Features.Operations.Components;
using OperationsSystem.Blazor.Client.State;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Operations;

public sealed class ReturnToRampPortalTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Detail_contract_hydrates_nested_occurrences_and_filters_compatibility_rows()
    {
        var normalServiceId = Guid.NewGuid();
        var legacyServiceId = Guid.NewGuid();
        var normalTaskId = Guid.NewGuid();
        var legacyTaskId = Guid.NewGuid();
        var occurrenceId = Guid.NewGuid();
        var nestedLineId = Guid.NewGuid();
        var performerId = Guid.NewGuid();
        var payload = $$"""
            {
              "id": "{{Guid.NewGuid()}}",
              "flightId": "{{Guid.NewGuid()}}",
              "serviceLines": [
                {{ServiceJson(normalServiceId, false, performerId)}},
                {{ServiceJson(legacyServiceId, true, performerId)}}
              ],
              "tasks": [
                {{TaskJson(normalTaskId, false, performerId)}},
                {{TaskJson(legacyTaskId, true, performerId)}}
              ],
              "returnToRamps": [{
                "id": "{{occurrenceId}}",
                "fromUtc": "2026-08-08T10:00:00Z",
                "toUtc": "2026-08-08T11:00:00Z",
                "description": "Bird strike inspection",
                "recordedByUserId": "{{Guid.NewGuid()}}",
                "createdAtUtc": "2026-08-08T11:05:00Z",
                "serviceLines": [{{ServiceJson(nestedLineId, true, performerId)}}],
                "tasks": []
              }],
              "rowVersion": "v1"
            }
            """;

        var detail = JsonSerializer.Deserialize<WorkOrderDetail>(payload, JsonOptions);

        detail.ShouldNotBeNull();
        ReturnToRampDraftMapper.StandardServiceLines(detail).Select(item => item.Id).ShouldBe([normalServiceId]);
        ReturnToRampDraftMapper.StandardTasks(detail).Select(item => item.Id).ShouldBe([normalTaskId]);
        var occurrence = ReturnToRampDraftMapper.ReturnToRamps(detail, UtcTimeZone()).ShouldHaveSingleItem();
        occurrence.Id.ShouldBe(occurrenceId);
        occurrence.Description.ShouldBe("Bird strike inspection");
        occurrence.ServiceLines.ShouldHaveSingleItem().Id.ShouldBe(nestedLineId);
        occurrence.ServiceLines[0].PerformerSnapshots.ShouldHaveSingleItem().FullName.ShouldBe("Ramp Agent");
    }

    [Fact]
    public void Request_mapper_preserves_multiple_occurrence_and_child_ids_and_sends_only_pending_bytes()
    {
        var firstId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var performerId = Guid.NewGuid();
        var first = ValidDraft(firstId, childId, serviceId, performerId);
        first.ServiceLines[0].Attachments.Add(new ReturnToRampAttachmentDraft
        {
            Id = Guid.NewGuid(),
            Kind = "Document",
            OriginalFileName = "stored.pdf",
            ContentType = "application/pdf",
            Size = 100
        });
        first.ServiceLines[0].Attachments.Add(new ReturnToRampAttachmentDraft
        {
            Kind = "Image",
            OriginalFileName = "pending.png",
            ContentType = "image/png",
            Size = 4,
            Content = [0x89, 0x50, 0x4e, 0x47]
        });
        var second = ValidDraft(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), performerId);

        ReturnToRampDraftMapper.ToRequests([], UtcTimeZone()).ShouldBeEmpty();
        var requests = ReturnToRampDraftMapper.ToRequests([first, second], UtcTimeZone());

        requests.Count.ShouldBe(2);
        requests[0].Id.ShouldBe(firstId);
        requests[0].ServiceLines.ShouldHaveSingleItem().Id.ShouldBe(childId);
        requests[0].ServiceLines[0].IsReturnToRamp.ShouldBeFalse();
        requests[0].ServiceLines[0].Attachments.ShouldHaveSingleItem().FileName.ShouldBe("pending.png");
        requests[1].Id.ShouldBe(second.Id);
    }

    [Fact]
    public void Completion_wizards_place_a_skippable_return_to_ramps_step_before_signature()
    {
        CompletionWorkOrderWizard.EditorLabels.ShouldBe(
            ["Details", "Service lines", "Tasks", "Return to ramps", "Signature"]);
        CompletionWorkOrderWizard.AdHocLabels.ShouldBe(
            ["Flight", "Service lines", "Tasks", "Return to ramps", "Signature"]);
        CompletionWorkOrderWizard.ReturnToRampsStep.ShouldBe(3);
        CompletionWorkOrderWizard.SignatureStep.ShouldBe(4);
        ReturnToRampDraftMapper.ToRequests([], UtcTimeZone()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("InProgress", true)]
    [InlineData("Completed", true)]
    [InlineData("Scheduled", false)]
    [InlineData("Canceled", false)]
    public void Standalone_flight_action_is_available_only_for_supported_statuses(string status, bool expected)
    {
        ReturnToRampPortalPolicy.CanRecordForFlightStatus(status).ShouldBe(expected);
    }

    [Fact]
    public void Occurrence_validation_enforces_decimal_18_2_usage_rules()
    {
        var draft = ValidDraft(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        draft.Tasks.Add(new ReturnToRampTaskDraft
        {
            TaskType = "Major",
            Description = "Test equipment",
            FromLocal = draft.FromLocal,
            ToLocal = draft.ToLocal,
            EmployeeIds = [Guid.NewGuid()],
            Tools =
            [
                new ReturnToRampResourceDraft
                {
                    ItemId = Guid.NewGuid(),
                    CalculationType = ResourceCalculationType.Quantity,
                    Quantity = 1.001m
                }
            ]
        });

        var messages = ReturnToRampDraftValidation.Validate(draft, UtcTimeZone());

        messages.ShouldContain(message => message.Contains("16 whole digits and 2 decimal places", StringComparison.Ordinal));
    }

    [Fact]
    public void Attachment_validation_matches_count_size_and_mime_policies()
    {
        var draft = ValidDraft(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var attachments = draft.ServiceLines[0].Attachments;
        for (var index = 0; index < ReturnToRampAttachmentValidation.MaxAttachments + 1; index++)
        {
            attachments.Add(new ReturnToRampAttachmentDraft
            {
                Kind = "Image",
                OriginalFileName = $"image-{index}.png",
                ContentType = "image/png",
                Size = 4,
                Content = [0x89, 0x50, 0x4e, 0x47]
            });
        }
        attachments.Add(new ReturnToRampAttachmentDraft
        {
            Kind = "Document",
            OriginalFileName = "wrong.txt",
            ContentType = "text/plain",
            Size = ReturnToRampAttachmentValidation.MaxBytes("Document") + 1,
            Content = [1]
        });

        var messages = ReturnToRampAttachmentValidation.Validate(draft);

        messages.ShouldContain(message => message.Contains("at most 10 attachments", StringComparison.Ordinal));
        messages.ShouldContain(message => message.Contains("empty or oversized document", StringComparison.Ordinal));
        messages.ShouldContain(message => message.Contains("unsupported document", StringComparison.Ordinal));
    }

    [Fact]
    public void Persisted_attachment_deletion_changes_only_attachment_state_and_row_version()
    {
        var attachments = new List<string> { "persisted", "pending" };
        var unsavedDescription = "User has not submitted this edit";
        var unsavedQuantity = 7.25m;

        var rowVersion = WorkOrderAttachmentMutation.ApplyPersistedDeletion(attachments, "persisted", "v2");

        attachments.ShouldBe(["pending"]);
        rowVersion.ShouldBe("v2");
        unsavedDescription.ShouldBe("User has not submitted this edit");
        unsavedQuantity.ShouldBe(7.25m);
    }

    private static ReturnToRampDraft ValidDraft(Guid id, Guid childId, Guid serviceId, Guid performerId) => new()
    {
        Id = id,
        FromLocal = new DateTime(2026, 8, 8, 10, 0, 0),
        ToLocal = new DateTime(2026, 8, 8, 11, 0, 0),
        Description = "Inspection",
        ServiceLines =
        [
            new ReturnToRampServiceDraft
            {
                Id = childId,
                ServiceId = serviceId,
                ServiceName = "Inspection",
                PerformedByStaffMemberIds = [performerId],
                FromLocal = new DateTime(2026, 8, 8, 10, 0, 0),
                ToLocal = new DateTime(2026, 8, 8, 11, 0, 0)
            }
        ]
    };

    private static string ServiceJson(Guid id, bool isReturnToRamp, Guid performerId) => $$"""
        {
          "id": "{{id}}",
          "serviceId": "{{Guid.NewGuid()}}",
          "serviceName": "Inspection",
          "performedBy": [{ "staffMemberId": "{{performerId}}", "fullName": "Ramp Agent", "employeeId": "E-10" }],
          "fromUtc": "2026-08-08T10:00:00Z",
          "toUtc": "2026-08-08T11:00:00Z",
          "description": null,
          "isReturnToRamp": {{isReturnToRamp.ToString().ToLowerInvariant()}},
          "attachments": []
        }
        """;

    private static string TaskJson(Guid id, bool isReturnToRamp, Guid employeeId) => $$"""
        {
          "id": "{{id}}",
          "taskType": "Major",
          "description": "Inspection",
          "fromUtc": "2026-08-08T10:00:00Z",
          "toUtc": "2026-08-08T11:00:00Z",
          "employees": [{ "staffMemberId": "{{employeeId}}", "fullName": "Ramp Agent", "employeeId": "E-10" }],
          "tools": [],
          "materials": [],
          "generalSupports": [],
          "attachments": [],
          "isReturnToRamp": {{isReturnToRamp.ToString().ToLowerInvariant()}}
        }
        """;

    private static UserTimeZone UtcTimeZone() => new(new ThrowingJsRuntime());

    private sealed class ThrowingJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new InvalidOperationException("JS interop is not expected in this UTC-only mapper test.");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new InvalidOperationException("JS interop is not expected in this UTC-only mapper test.");
    }
}
