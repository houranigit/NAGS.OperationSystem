using System.Reflection;
using System.Text.Json;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Features.Operations.Components;
using OperationsSystem.Blazor.Client.State;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Operations;

public sealed class WorkOrderReferencePortalTests
{
    private static readonly DateTime Start = new(2026, 10, 30, 10, 0, 0);
    private static readonly Guid First = Guid.NewGuid();
    private static readonly Guid Second = Guid.NewGuid();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Employee_selection_survives_dropdown_reset_and_supports_add_remove_and_clear(bool task)
    {
        var savedPeriod = new EmployeeAssignmentDraft(First, Start.AddMinutes(5), Start.AddMinutes(30));
        var line = new ReturnToRampServiceDraft { FromLocal = Start, EmployeeAssignments = [savedPeriod] };
        var activity = new ReturnToRampTaskDraft { FromLocal = Start, EmployeeAssignments = [savedPeriod] };

        void Select(IEnumerable<Guid>? ids)
        {
            if (task) ReturnToRampEditorDialog.SelectTaskEmployees(activity, ids);
            else ReturnToRampEditorDialog.SelectServiceEmployees(line, ids);
        }

        IEnumerable<Guid> Selected() => task ? activity.EmployeeIds : line.PerformedByStaffMemberIds;
        IReadOnlyList<EmployeeAssignmentDraft> Periods() => task ? activity.EmployeeAssignments : line.EmployeeAssignments;

        // The dropdown yields a query over its selection, then clears that set when Data changes.
        var dropdownSelection = new HashSet<Guid> { First, Second };
        Select(dropdownSelection.Select(id => id));
        dropdownSelection.Clear();

        Selected().ShouldBe([First, Second], ignoreOrder: true);
        Periods().Single(item => item.StaffMemberId == First).ShouldBe(savedPeriod);
        Periods().Single(item => item.StaffMemberId == Second).FromLocal.ShouldBe(Start);
        Periods().Single(item => item.StaffMemberId == Second).ToLocal.ShouldBeNull();

        Select([Second]);
        Selected().ShouldBe([Second]);
        Periods().ShouldHaveSingleItem().StaffMemberId.ShouldBe(Second);
        Select(null);
        Selected().ShouldBeEmpty();
        Periods().ShouldBeEmpty();
        Select([First]);
        Periods().ShouldHaveSingleItem().FromLocal.ShouldBe(Start);
        Periods()[0].ToLocal.ShouldBeNull();
    }

    [Fact]
    public void Editing_a_work_order_retains_references_in_normal_and_return_to_ramp_requests()
    {
        var detail = Detail();
        var editor = new WorkOrderEditorDialog();
        var componentType = typeof(WorkOrderEditorDialog);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        componentType.GetProperty("UserTimeZone", flags)!.SetValue(editor, Utc());
        var model = componentType.GetMethod("FromWorkOrder", flags)!.Invoke(editor, [detail]);
        componentType.GetField("model", flags)!.SetValue(editor, model);

        var request = (WorkOrderRequestModel)componentType.GetMethod("BuildRequest", flags)!.Invoke(editor, null)!;

        request.WorkPackageNumber.ShouldBe("WP-001");
        request.ServiceLines.ShouldHaveSingleItem().CustomerReferenceNumber.ShouldBe("SERVICE-001");
        request.Tasks.ShouldHaveSingleItem().CustomerReferenceNumber.ShouldBe("TASK-001");
        var occurrence = request.ReturnToRamps.ShouldHaveSingleItem();
        occurrence.ServiceLines.ShouldHaveSingleItem().CustomerReferenceNumber.ShouldBe("RTR-SERVICE-001");
        occurrence.Tasks.ShouldHaveSingleItem().CustomerReferenceNumber.ShouldBe("RTR-TASK-001");
    }

    [Fact]
    public void Canceling_return_to_ramp_edit_keeps_original_references_and_merge_preserves_them()
    {
        var detail = Detail();
        var draft = ReturnToRampDraftMapper.ReturnToRamps(detail, Utc()).ShouldHaveSingleItem();
        var edited = draft.Clone();
        edited.ServiceLines[0].CustomerReferenceNumber = "new service reference";
        edited.Tasks[0].CustomerReferenceNumber = "new task reference";

        draft.ServiceLines[0].CustomerReferenceNumber.ShouldBe("RTR-SERVICE-001");
        draft.Tasks[0].CustomerReferenceNumber.ShouldBe("RTR-TASK-001");
        var requests = WorkOrderMergeMapping.BuildCanonicalReturnToRamps([detail]);
        requests.ShouldHaveSingleItem().ServiceLines[0].CustomerReferenceNumber.ShouldBe("RTR-SERVICE-001");
        requests![0].Tasks[0].CustomerReferenceNumber.ShouldBe("RTR-TASK-001");
        WorkOrderMergeMapping.ToRequest(detail.ServiceLines[0]).CustomerReferenceNumber.ShouldBe("SERVICE-001");
        WorkOrderMergeMapping.ToRequest(detail.Tasks[0]).CustomerReferenceNumber.ShouldBe("TASK-001");
    }

    private static WorkOrderDetail Detail()
    {
        var start = new DateTimeOffset(Start, TimeSpan.Zero);
        WorkOrderServiceLineModel Service(string reference) => new(Guid.NewGuid(), Guid.NewGuid(), "Inspection",
            [new(First, "Employee", "E-1", start, start.AddHours(1))], start, start.AddHours(1), null,
            CustomerReferenceNumber: reference);
        WorkOrderTaskModel Task(string reference) => new(Guid.NewGuid(), "Major", null, start, start.AddHours(1),
            [new(First, "Employee", "E-1", start, start.AddHours(1))], [], [], [], [],
            CustomerReferenceNumber: reference);
        // Deserialize a detail envelope to exercise the independent portal/API contract.
        var envelope = new
        {
            id = Guid.NewGuid(), flightId = Guid.NewGuid(), type = "Completion", actualFlightNumber = "123",
            workPackageNumber = " WP-001 ", serviceLines = new[] { Service("SERVICE-001") }, tasks = new[] { Task("TASK-001") },
            returnToRamps = new[] { new WorkOrderReturnToRampModel(Guid.NewGuid(), start, start.AddHours(1), null,
                Guid.NewGuid(), start, [Service("RTR-SERVICE-001")], [Task("RTR-TASK-001")]) }
        };
        return JsonSerializer.Deserialize<WorkOrderDetail>(JsonSerializer.Serialize(envelope, JsonOptions), JsonOptions)!;
    }

    private static UserTimeZone Utc() => new(new NoJs());

    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new InvalidOperationException("This mapper test does not use JavaScript.");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new InvalidOperationException("This mapper test does not use JavaScript.");
    }
}
