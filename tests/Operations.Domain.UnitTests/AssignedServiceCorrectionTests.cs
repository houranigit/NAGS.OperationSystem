using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class AssignedServiceCorrectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Assigned_staff_can_delete_contributed_work_but_an_unassigned_capability_cannot(bool task)
    {
        var editor = TestData.Staff(); var author = Guid.NewGuid(); var editorUser = Guid.NewGuid(); var closer = Guid.NewGuid();
        var flight = Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(), TestData.FlightNo(),
            TestData.Schedule(), null, [TestData.Service()], [editor], author, TestData.Now).Value;
        var id = Guid.NewGuid(); var performerId = Guid.NewGuid(); var service = TestData.Service();
        var open = TimeWindow.Create(TestData.Now, null).Value; var closed = TimeWindow.Create(TestData.Now, TestData.Now.AddMinutes(20)).Value;
        if (task)
        {
            var performers = new[] { new FlightTaskPerformerInput(editor, closed, null, performerId) };
            flight.SaveTask(id, FlightTaskType.Major, null, null, open, null, performers, [], [], [], author, TestData.Now).IsSuccess.ShouldBeTrue();
            flight.SaveTask(id, FlightTaskType.Major, null, null, closed, null, performers, [], [], [], closer, TestData.Now).IsSuccess.ShouldBeTrue();
            flight.RemoveTask(id, editorUser, TestData.Now, assignedEditorStaffMemberId: Guid.NewGuid()).Error.Code.ShouldBe("Operations.FlightTask.DeleteReadOnly");
            flight.RemoveTask(id, editorUser, TestData.Now, assignedEditorStaffMemberId: editor.StaffMemberId).Value.CreatedByUserId.ShouldBe(author);
            flight.Tasks.ShouldBeEmpty();
        }
        else
        {
            var performers = new[] { new FlightServicePerformerInput(editor, closed, performerId) };
            flight.SaveService(id, service, null, open, null, performers, author, TestData.Now).IsSuccess.ShouldBeTrue();
            flight.SaveService(id, service, null, closed, null, performers, closer, TestData.Now).IsSuccess.ShouldBeTrue();
            flight.RemoveService(id, editorUser, TestData.Now, assignedEditorStaffMemberId: Guid.NewGuid()).Error.Code.ShouldBe("Operations.FlightService.DeleteReadOnly");
            flight.RemoveService(id, editorUser, TestData.Now, assignedEditorStaffMemberId: editor.StaffMemberId).Value.CreatedByUserId.ShouldBe(author);
            flight.Services.ShouldBeEmpty();
        }
    }

    [Fact]
    public void Changing_a_contributed_service_selection_requires_a_real_assigned_staff_capability()
    {
        var editor = TestData.Staff(); var author = Guid.NewGuid(); var closer = Guid.NewGuid(); var serviceId = Guid.NewGuid();
        var flight = Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(), TestData.FlightNo(),
            TestData.Schedule(), null, [TestData.Service()], [editor], author, TestData.Now).Value;
        var service = TestData.Service(); var window = TimeWindow.Create(TestData.Now, TestData.Now.AddMinutes(20)).Value;
        var performers = new[] { new FlightServicePerformerInput(TestData.Staff(), window, Guid.NewGuid()) };
        flight.SaveService(serviceId, service, null, TimeWindow.Create(TestData.Now, null).Value, null, performers, author, TestData.Now).IsSuccess.ShouldBeTrue();
        flight.SaveService(serviceId, service, null, window, null, performers, closer, TestData.Now).IsSuccess.ShouldBeTrue();
        var replacement = TestData.Service();

        flight.SaveService(serviceId, replacement, null, window, null, performers, author, TestData.Now,
            Guid.NewGuid()).Error.Code.ShouldBe("Operations.FlightService.SelectionReadOnly");
        var corrected = flight.SaveService(serviceId, replacement, null, window, null, performers, author, TestData.Now,
            editor.StaffMemberId);

        corrected.IsSuccess.ShouldBeTrue(); corrected.Value.Id.ShouldBe(serviceId); corrected.Value.CreatedByUserId.ShouldBe(author);
        corrected.Value.EndedByUserId.ShouldBe(closer); corrected.Value.Service.ShouldBe(replacement);
    }
}
