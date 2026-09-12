using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Features.Operations.Components;
using OperationsSystem.Blazor.Client.State;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Operations;

public sealed class WorkOrderEntryRulesTests
{
    private static readonly DateTime Start = new(2026, 9, 12, 10, 0, 0);
    private static readonly DateTime End = Start.AddHours(1);
    private static readonly Guid FirstEmployee = Guid.NewGuid();
    private static readonly Guid SecondEmployee = Guid.NewGuid();

    [Fact]
    public void Changing_employees_retains_existing_periods_and_requires_times_for_new_people()
    {
        var saved = new EmployeeAssignmentDraft(FirstEmployee, Start, End);
        var removed = new EmployeeAssignmentDraft(Guid.NewGuid(), Start, End);

        var assignments = WorkOrderEntryRules.SynchronizeAssignments(
            [FirstEmployee, SecondEmployee, FirstEmployee], [saved, removed]);

        assignments.Count.ShouldBe(2);
        assignments[0].ShouldBe(saved);
        assignments[1].FromLocal.ShouldBeNull();
        assignments[1].ToLocal.ShouldBeNull();
        WorkOrderEntryRules.ValidateAssignments([FirstEmployee, SecondEmployee], assignments, Start, End, Utc(), "Service 1")
            .ShouldContain(message => message.Contains("every selected employee"));
    }

    [Fact]
    public void Every_selected_employee_requires_an_assignment()
    {
        WorkOrderEntryRules.ValidateAssignments([FirstEmployee], [], Start, End, Utc(), "Task 1")
            .ShouldContain(message => message.Contains("every selected employee"));
    }

    [Theory]
    [InlineData(-1, 30, "cannot be before the service/task")]
    [InlineData(30, 61, "cannot be after the service/task")]
    [InlineData(40, 30, "cannot be before From")]
    public void Employee_periods_reject_outside_or_reversed_times(int fromMinutes, int toMinutes, string expected)
    {
        WorkOrderEntryRules.ValidateAssignments([FirstEmployee],
            [new(FirstEmployee, Start.AddMinutes(fromMinutes), Start.AddMinutes(toMinutes))], Start, End, Utc(), "Service 1")
            .ShouldContain(message => message.Contains(expected));
    }

    [Fact]
    public void Employees_can_have_different_periods_inside_the_same_service()
    {
        IReadOnlyList<EmployeeAssignmentDraft> assignments =
            [new(FirstEmployee, Start, End), new(SecondEmployee, Start.AddMinutes(15), End.AddMinutes(-15))];

        WorkOrderEntryRules.ValidateAssignments([FirstEmployee, SecondEmployee], assignments, Start, End, Utc(), "Service 1")
            .ShouldBeEmpty();
        var request = WorkOrderEntryRules.ToRequests(assignments, Utc());
        request[0].FromUtc.ShouldBe(new DateTimeOffset(Start, TimeSpan.Zero));
        request[1].ToUtc.ShouldBe(new DateTimeOffset(End.AddMinutes(-15), TimeSpan.Zero));
    }

    [Theory]
    [InlineData("60000000-0000-0000-0000-000000000001")]
    [InlineData("70000000-0000-0000-0000-000000000001")]
    [InlineData("80000000-0000-0000-0000-000000000001")]
    public void Unknown_resources_require_descriptions_in_return_to_ramp_tasks(string resourceId)
    {
        var resource = new ReturnToRampResourceDraft { ItemId = Guid.Parse(resourceId) };
        var task = new ReturnToRampTaskDraft
        {
            FromLocal = Start, ToLocal = End, EmployeeIds = [FirstEmployee],
            EmployeeAssignments = [new(FirstEmployee, Start, End)], Tools = [resource]
        };
        var draft = new ReturnToRampDraft { FromLocal = Start, ToLocal = End, Tasks = [task] };

        ReturnToRampDraftValidation.Validate(draft, Utc()).ShouldContain(message => message.Contains("description for the Unknown item"));
        resource.Description = "Borrowed inspection equipment";
        ReturnToRampDraftValidation.Validate(draft, Utc()).ShouldBeEmpty();
    }

    [Fact]
    public void Unknown_service_requires_description_and_uses_the_seed_id()
    {
        WorkOrderEntryRules.UnknownService.ShouldBe(Guid.Parse("40000000-0000-0000-0000-000000000003"));
        var line = new ReturnToRampServiceDraft
        {
            ServiceId = WorkOrderEntryRules.UnknownService,
            FromLocal = Start, ToLocal = End, PerformedByStaffMemberIds = [FirstEmployee],
            EmployeeAssignments = [new(FirstEmployee, Start, End)]
        };
        var draft = new ReturnToRampDraft { FromLocal = Start, ToLocal = End, ServiceLines = [line] };
        ReturnToRampDraftValidation.Validate(draft, Utc()).ShouldContain(message => message.Contains("description for the Unknown service"));
        line.Description = "Additional cabin inspection";
        ReturnToRampDraftValidation.Validate(draft, Utc()).ShouldBeEmpty();
        WorkOrderEntryRules.RequiresServiceDescription(Guid.NewGuid()).ShouldBeFalse();
    }

    [Fact]
    public void Only_seeded_unknown_customer_requires_customer_description()
    {
        WorkOrderEntryRules.RequiresCustomerDescription(Guid.Parse("50000000-0000-0000-0000-000000000001")).ShouldBeTrue();
        WorkOrderEntryRules.RequiresCustomerDescription(Guid.NewGuid()).ShouldBeFalse();
        WorkOrderEntryRules.RequiresCustomerDescription(null).ShouldBeFalse();
    }

    [Fact]
    public void Merge_preserves_individual_periods_and_resource_descriptions()
    {
        var from = new DateTimeOffset(Start, TimeSpan.Zero);
        var task = new WorkOrderTaskModel(Guid.NewGuid(), "Major", "Inspection", from, from.AddHours(1),
            [new(FirstEmployee, "Employee", "E-1", from.AddMinutes(10), from.AddMinutes(40))],
            [new(Guid.NewGuid(), "Unknown", ResourceCalculationType.Quantity, 1, null, null, "Borrowed tool")],
            [new(Guid.NewGuid(), "Unknown", ResourceCalculationType.Quantity, 1, null, null, "Sealant")],
            [new(Guid.NewGuid(), "Unknown", ResourceCalculationType.Quantity, 1, null, null, "External technician")], []);

        var request = WorkOrderMergeMapping.ToRequest(task);

        request.EmployeeAssignments.ShouldHaveSingleItem().FromUtc.ShouldBe(from.AddMinutes(10));
        request.EmployeeAssignments![0].ToUtc.ShouldBe(from.AddMinutes(40));
        request.Tools.ShouldHaveSingleItem().Description.ShouldBe("Borrowed tool");
        request.Materials.ShouldHaveSingleItem().Description.ShouldBe("Sealant");
        request.GeneralSupports.ShouldHaveSingleItem().Description.ShouldBe("External technician");
    }

    [Fact]
    public void Pdf_document_limit_is_two_megabytes_and_includes_exact_boundary()
    {
        ReturnToRampAttachmentValidation.MaxBytes("Document").ShouldBe(2 * 1024 * 1024);
        var attachment = new ReturnToRampAttachmentDraft
        {
            Kind = "Document", ContentType = "application/pdf", Content = [1], Size = 2 * 1024 * 1024
        };
        var draft = new ReturnToRampDraft { ServiceLines = [new() { Attachments = [attachment] }] };
        ReturnToRampAttachmentValidation.Validate(draft).ShouldBeEmpty();
        attachment.Size++;
        ReturnToRampAttachmentValidation.Validate(draft).ShouldContain(message => message.Contains("oversized document"));
    }

    private static UserTimeZone Utc() => new(new NoJsRuntime());

    private sealed class NoJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new InvalidOperationException();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new InvalidOperationException();
    }
}
