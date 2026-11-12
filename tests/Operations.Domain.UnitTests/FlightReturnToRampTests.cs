using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Resources;
using MasterData.Contracts.Seeding;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class FlightReturnToRampTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(" \t ", null)]
    [InlineData("  Returned to stand  ", "Returned to stand")]
    public void ArrivalDetails_NormalizesUtcAndOptionalRemarks_AndCopiesTheReceivingEmployee(string? remarks, string? expectedRemarks)
    {
        var employee = TestData.Staff();

        var details = RtrArrivalDetails.Create(TestData.Now.ToOffset(TimeSpan.FromHours(3)), employee, remarks).Value;

        details.ArrivedAtUtc.ShouldBe(TestData.Now);
        details.ArrivedAtUtc.Offset.ShouldBe(TimeSpan.Zero);
        details.ReceivedByEmployee.ShouldBe(employee);
        details.ReceivedByEmployee.ShouldNotBeSameAs(employee);
        details.Remarks.ShouldBe(expectedRemarks);
        RtrArrivalDetails.Create(TestData.Now, employee, $" {new string('R', 2000)} ").IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("time")]
    [InlineData("employee")]
    [InlineData("employee-id")]
    [InlineData("remarks")]
    public void ArrivalDetails_RequiresTimeAndAnIdentifiedEmployee_AndLimitsRemarks(string invalidInput)
    {
        StaffMemberSnapshot? employee = invalidInput switch
        {
            "employee" => null,
            "employee-id" => new StaffMemberSnapshot(Guid.Empty, "Receiver", "E1"),
            _ => TestData.Staff()
        };

        var result = RtrArrivalDetails.Create(invalidInput == "time" ? default : TestData.Now, employee,
            invalidInput == "remarks" ? new string('R', 2001) : null);

        result.IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DepartureDetails_AllowsAnOptionalSignature_AndCopiesUtcEmployeeAndSignatureMetadata(bool signed)
    {
        var employee = TestData.Staff();
        var signature = signed ? Signature() : null;

        var details = RtrDepartureDetails.Create(TestData.Now.ToOffset(TimeSpan.FromHours(3)), signature, employee, " Departed ").Value;

        details.DepartedAtUtc.ShouldBe(TestData.Now);
        details.DepartedAtUtc.Offset.ShouldBe(TimeSpan.Zero);
        details.HandledByEmployee.ShouldBe(employee);
        details.HandledByEmployee.ShouldNotBeSameAs(employee);
        details.CustomerSignature.ShouldBe(signature);
        if (signed)
            details.CustomerSignature.ShouldNotBeSameAs(signature);
        details.Remarks.ShouldBe("Departed");
        RtrDepartureDetails.Create(TestData.Now, null, employee, " ").Value.Remarks.ShouldBeNull();
        RtrDepartureDetails.Create(TestData.Now, null, employee, $" {new string('R', 2000)} ").IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("time")]
    [InlineData("employee")]
    [InlineData("employee-id")]
    [InlineData("remarks")]
    public void DepartureDetails_RequiresTimeAndAnIdentifiedHandler_AndLimitsRemarks(string invalidInput)
    {
        StaffMemberSnapshot? employee = invalidInput switch
        {
            "employee" => null,
            "employee-id" => new StaffMemberSnapshot(Guid.Empty, "Handler", "E1"),
            _ => TestData.Staff()
        };

        var result = RtrDepartureDetails.Create(invalidInput == "time" ? default : TestData.Now, null, employee,
            invalidInput == "remarks" ? new string('R', 2001) : null);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void StartReturnToRamp_RequiresADepartureSubmission_AndDoesNotAllocateAnOccurrenceOnFailure()
    {
        var flight = ScheduleFlight();

        var result = flight.StartReturnToRamp(Arrival(), Guid.NewGuid(), TestData.Now);

        result.Error.Code.ShouldBe("Operations.ReturnToRamp.DepartureRequired");
        flight.ReturnToRamps.ShouldBeEmpty();
        flight.LastReturnToRampSequence.ShouldBe(0);
        flight.UpdatedAtUtc.ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnyDepartureSubmission_IncludingUnacceptedConflicts_AllowsOneOpenOccurrenceWithoutAnAtdBound(bool pending)
    {
        var flight = DepartedFlight(pending);
        var departure = flight.DepartureSubmissions[0];
        var arrival = Arrival(TestData.Now.AddHours(-2));
        var user = Guid.NewGuid();

        var rtr = flight.StartReturnToRamp(arrival, user, TestData.Now.ToOffset(TimeSpan.FromHours(3))).Value;

        flight.ReturnToRamps.ShouldHaveSingleItem().ShouldBeSameAs(rtr);
        rtr.Id.ShouldNotBe(Guid.Empty);
        rtr.FlightId.ShouldBe(flight.Id);
        rtr.Sequence.ShouldBe(1);
        flight.LastReturnToRampSequence.ShouldBe(1);
        rtr.ArrivalDetails.ShouldBe(arrival);
        rtr.ArrivalDetails.ShouldNotBeSameAs(arrival);
        rtr.DepartureDetails.ShouldBeNull();
        rtr.IsOpen.ShouldBeTrue();
        rtr.Window.FromUtc.ShouldBe(arrival.ArrivedAtUtc);
        rtr.Window.ToUtc.ShouldBeNull();
        rtr.CreatedByUserId.ShouldBe(user);
        rtr.CreatedAtUtc.ShouldBe(TestData.Now);
        rtr.CreatedAtUtc.Offset.ShouldBe(TimeSpan.Zero);
        rtr.EndedByUserId.ShouldBeNull();
        rtr.UpdatedByUserId.ShouldBeNull();
        rtr.UpdatedAtUtc.ShouldBeNull();
        rtr.Services.ShouldBeEmpty();
        rtr.Tasks.ShouldBeEmpty();
        departure.Details.Atd.ShouldBe(TestData.Now.AddHours(1));
        flight.DepartureSubmissions.ShouldAllBe(item => item.IsAccepted == !pending);
        flight.Schedule.ShouldBe(TestData.Schedule());
    }

    [Theory]
    [InlineData(true, "Operations.ReturnToRamp.ArrivalDetailsRequired")]
    [InlineData(false, "Operations.ReturnToRamp.UserRequired")]
    public void InvalidStartInput_DoesNotAllocateASequenceOrChangeTheFlight(bool missingArrival, string code)
    {
        var flight = DepartedFlight();
        var previousTime = flight.UpdatedAtUtc;

        var result = flight.StartReturnToRamp(missingArrival ? null : Arrival(),
            missingArrival ? Guid.NewGuid() : Guid.Empty, TestData.Now.AddMinutes(1));

        result.Error.Code.ShouldBe(code);
        flight.ReturnToRamps.ShouldBeEmpty();
        flight.LastReturnToRampSequence.ShouldBe(0);
        flight.UpdatedAtUtc.ShouldBe(previousTime);
    }

    [Fact]
    public void CancellationBlocksRtrStart_AndAnExistingOccurrencePreventsCancellation()
    {
        var canceled = ScheduleFlight();
        canceled.SubmitCancellation(TestData.Staff(), CancellationDetails.Create(TestData.Now, null).Value,
            Guid.NewGuid(), TestData.Now).IsSuccess.ShouldBeTrue();

        var start = canceled.StartReturnToRamp(Arrival(), Guid.NewGuid(), TestData.Now.AddMinutes(1));

        start.Error.Code.ShouldBe("Operations.Flight.CancellationSubmitted");
        canceled.ReturnToRamps.ShouldBeEmpty();
        canceled.LastReturnToRampSequence.ShouldBe(0);
        canceled.UpdatedAtUtc.ShouldBe(TestData.Now);

        var departed = DepartedFlight();
        var rtr = Start(departed);
        var cancel = departed.SubmitCancellation(TestData.Staff(), CancellationDetails.Create(TestData.Now, null).Value,
            Guid.NewGuid(), TestData.Now.AddMinutes(1));

        cancel.IsFailure.ShouldBeTrue();
        departed.ReturnToRamps.ShouldHaveSingleItem().ShouldBeSameAs(rtr);
        departed.CancellationSubmissions.ShouldBeEmpty();
    }

    [Fact]
    public void AnOpenOccurrence_BlocksAnotherStartWithoutConsumingASequence()
    {
        var flight = DepartedFlight();
        var first = Start(flight);
        var previousTime = flight.UpdatedAtUtc;

        var result = flight.StartReturnToRamp(Arrival(TestData.Now.AddHours(2)), Guid.NewGuid(), TestData.Now.AddMinutes(1));

        result.Error.Code.ShouldBe("Operations.ReturnToRamp.OpenExists");
        flight.ReturnToRamps.ShouldHaveSingleItem().ShouldBeSameAs(first);
        flight.LastReturnToRampSequence.ShouldBe(1);
        first.IsOpen.ShouldBeTrue();
        flight.UpdatedAtUtc.ShouldBe(previousTime);
    }

    [Fact]
    public void OccurrenceNumbers_AreStableAcrossCorrections_AndNotReusedAfterRemoval()
    {
        var flight = DepartedFlight();
        var first = Start(flight);
        AddCompletedService(flight, first);
        Close(flight, first);
        var second = flight.StartReturnToRamp(Arrival(TestData.Now.AddHours(-3)), Guid.NewGuid(), TestData.Now.AddMinutes(1)).Value;
        second.Sequence.ShouldBe(2);
        flight.UpdateReturnToRampArrival(second.Id, Arrival(TestData.Now.AddHours(-4)), Guid.NewGuid(), TestData.Now.AddMinutes(2))
            .Value.ShouldBeSameAs(second);

        var removed = flight.RemoveReturnToRamp(second.Id, Guid.NewGuid(), TestData.Now.AddMinutes(3)).Value;
        var third = Start(flight);

        removed.ShouldBeSameAs(second);
        second.Sequence.ShouldBe(2);
        first.Sequence.ShouldBe(1);
        third.Sequence.ShouldBe(3);
        flight.LastReturnToRampSequence.ShouldBe(3);
        flight.ReturnToRamps.Select(item => item.Id).ShouldBe([first.Id, third.Id]);
    }

    [Fact]
    public void ArrivalAndDepartureCorrections_PreserveIdentityChildrenAndCreator_AndEqualValuesAreNoOps()
    {
        var flight = DepartedFlight();
        var creator = Guid.NewGuid();
        var rtr = flight.StartReturnToRamp(Arrival(), creator, TestData.Now).Value;
        var service = AddCompletedService(flight, rtr);
        var performer = service.Performers.Single();
        var attachment = flight.AddServiceAttachment(service.Id, Attachment(), Guid.NewGuid(), TestData.Now, rtr.Id).Value;
        var editor = Guid.NewGuid();
        var correctedArrival = Arrival(TestData.Now.AddMinutes(-5), " Corrected arrival ");
        var editedAt = TestData.Now.AddMinutes(1).ToOffset(TimeSpan.FromHours(3));

        flight.UpdateReturnToRampArrival(rtr.Id, correctedArrival, editor, editedAt).Value.ShouldBeSameAs(rtr);

        rtr.ArrivalDetails.ShouldBe(correctedArrival);
        rtr.CreatedByUserId.ShouldBe(creator);
        rtr.CreatedAtUtc.ShouldBe(TestData.Now);
        rtr.UpdatedByUserId.ShouldBe(editor);
        rtr.UpdatedAtUtc.ShouldBe(editedAt.ToUniversalTime());
        rtr.Services.ShouldHaveSingleItem().ShouldBeSameAs(service);
        service.Performers.ShouldHaveSingleItem().ShouldBeSameAs(performer);
        service.Attachments.ShouldHaveSingleItem().ShouldBeSameAs(attachment);
        var equivalentArrival = RtrArrivalDetails.Create(correctedArrival.ArrivedAtUtc.ToOffset(TimeSpan.FromHours(3)),
            correctedArrival.ReceivedByEmployee, " Corrected arrival ").Value;
        flight.UpdateReturnToRampArrival(rtr.Id, equivalentArrival, Guid.NewGuid(), TestData.Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        rtr.UpdatedByUserId.ShouldBe(editor);
        rtr.UpdatedAtUtc.ShouldBe(editedAt.ToUniversalTime());

        var closer = Guid.NewGuid();
        var departure = Departure(TestData.Now.AddMinutes(30));
        flight.CloseReturnToRamp(rtr.Id, departure, closer, TestData.Now.AddMinutes(3)).Value.ShouldBeSameAs(rtr);
        rtr.IsOpen.ShouldBeFalse();
        rtr.Window.FromUtc.ShouldBe(correctedArrival.ArrivedAtUtc);
        rtr.Window.ToUtc.ShouldBe(departure.DepartedAtUtc);
        rtr.EndedByUserId.ShouldBe(closer);
        rtr.DepartureDetails.ShouldBe(departure);
        rtr.DepartureDetails.ShouldNotBeSameAs(departure);
        var closedAt = rtr.UpdatedAtUtc;
        var equivalentDeparture = RtrDepartureDetails.Create(departure.DepartedAtUtc.ToOffset(TimeSpan.FromHours(3)), null,
            departure.HandledByEmployee, " ").Value;
        flight.CloseReturnToRamp(rtr.Id, equivalentDeparture, Guid.NewGuid(), TestData.Now.AddMinutes(4)).IsSuccess.ShouldBeTrue();

        rtr.CreatedByUserId.ShouldBe(creator);
        rtr.EndedByUserId.ShouldBe(closer);
        rtr.UpdatedByUserId.ShouldBe(closer);
        rtr.UpdatedAtUtc.ShouldBe(closedAt);
        flight.UpdatedAtUtc.ShouldBe(closedAt);
        service.Attachments.ShouldHaveSingleItem().ShouldBeSameAs(attachment);
    }

    [Fact]
    public void NormalWorkAndEachOccurrence_HaveSeparateWriteScopes_AndCannotStealEachOthersChildren()
    {
        var flight = DepartedFlight();
        var user = Guid.NewGuid();
        var normalService = flight.AddService(TestData.Service(), null, Window(), null, user, TestData.Now).Value;
        var normalTask = flight.AddTask(FlightTaskType.Major, null, null, Window(), null, user, TestData.Now).Value;
        var first = Start(flight);
        var firstService = AddCompletedService(flight, first);
        var firstTask = AddCompletedTask(flight, first);
        Close(flight, first);
        var second = Start(flight);
        var secondService = AddCompletedService(flight, second);
        var secondTask = AddCompletedTask(flight, second);
        var previousTime = flight.UpdatedAtUtc;
        Result[] failures =
        [
            flight.UpdateService(firstService.Id, TestData.Service(), null, firstService.Window, null, user, TestData.Now.AddMinutes(1)),
            flight.UpdateTask(firstTask.Id, FlightTaskType.Minor, null, null, firstTask.Window, null, user, TestData.Now.AddMinutes(1)),
            flight.RemoveService(firstService.Id, user, TestData.Now.AddMinutes(1), second.Id),
            flight.RemoveTask(firstTask.Id, user, TestData.Now.AddMinutes(1), second.Id),
            flight.AddServiceAttachment(firstService.Id, Attachment(), user, TestData.Now.AddMinutes(1), second.Id),
            flight.AddTaskTool(firstTask.Id, Tool(), Duration(firstTask.Window.FromUtc), null, user, TestData.Now.AddMinutes(1), second.Id),
            flight.UpdateService(normalService.Id, TestData.Service(), null, normalService.Window, null, user, TestData.Now.AddMinutes(1), first.Id),
            flight.RemoveTask(normalTask.Id, user, TestData.Now.AddMinutes(1), first.Id)
        ];

        failures.ShouldAllBe(result => result.IsFailure);
        flight.Services.ShouldHaveSingleItem().ShouldBeSameAs(normalService);
        flight.Tasks.ShouldHaveSingleItem().ShouldBeSameAs(normalTask);
        flight.ServiceRecords.Count.ShouldBe(3);
        flight.TaskRecords.Count.ShouldBe(3);
        first.Services.ShouldHaveSingleItem().ShouldBeSameAs(firstService);
        first.Tasks.ShouldHaveSingleItem().ShouldBeSameAs(firstTask);
        second.Services.ShouldHaveSingleItem().ShouldBeSameAs(secondService);
        second.Tasks.ShouldHaveSingleItem().ShouldBeSameAs(secondTask);
        firstService.Attachments.ShouldBeEmpty();
        firstTask.Tools.ShouldBeEmpty();
        flight.UpdatedAtUtc.ShouldBe(previousTime);
    }

    [Fact]
    public void RtrTasks_RetainResourceModesAtaEmployeeAndAttachmentRulesWithinTheirOccurrence()
    {
        var flight = DepartedFlight();
        var rtr = Start(flight);
        var task = AddCompletedTask(flight, rtr);
        var user = Guid.NewGuid();
        var material = flight.AddTaskMaterial(task.Id, new MaterialSnapshot(Guid.NewGuid(), "Hydraulic fluid"),
            ResourceUsage.Create(ResourceCalculationType.Quantity, 2.25m, null, null).Value, " Supplied ", user, TestData.Now, rtr.Id).Value;
        var toolSnapshot = Tool();
        var tool = flight.AddTaskTool(task.Id, toolSnapshot, Duration(task.Window.FromUtc), null, user, TestData.Now, rtr.Id).Value;
        var attachment = flight.AddTaskAttachment(task.Id, Attachment(), user, TestData.Now, rtr.Id).Value;
        var employee = task.Performers.Single();
        Result[] failures =
        [
            flight.AddTaskTool(task.Id, toolSnapshot, Duration(task.Window.FromUtc), null, user, TestData.Now.AddMinutes(1), rtr.Id),
            flight.AddTaskMaterial(task.Id, new MaterialSnapshot(WellKnownMasterDataIds.UnknownMaterial, "Unknown Material"),
                ResourceUsage.Create(ResourceCalculationType.Quantity, 1, null, null).Value, null, user, TestData.Now.AddMinutes(1), rtr.Id),
            flight.AddTaskPerformer(task.Id, employee.Employee, employee.Window, null, user, TestData.Now.AddMinutes(1), rtr.Id),
            flight.AddService(TestData.PerLandingService(), null, task.Window, null, user, TestData.Now.AddMinutes(1), rtr.Id)
        ];

        failures.ShouldAllBe(result => result.IsFailure);
        task.Materials.ShouldHaveSingleItem().ShouldBeSameAs(material);
        material.Usage.Quantity.ShouldBe(2.25m);
        material.Notes.ShouldBe("Supplied");
        task.Tools.ShouldHaveSingleItem().ShouldBeSameAs(tool);
        tool.Usage.ToUtc.ShouldBeNull();
        task.Performers.ShouldHaveSingleItem().ShouldBeSameAs(employee);
        task.Attachments.ShouldHaveSingleItem().ShouldBeSameAs(attachment);
        task.AtaChapter.ShouldNotBeNull().Code.ShouldBe("32");
        Close(flight, rtr).IsOpen.ShouldBeFalse();
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("service-window")]
    [InlineData("service-employee")]
    [InlineData("service-employee-window")]
    [InlineData("task-window")]
    [InlineData("task-employee")]
    [InlineData("task-employee-window")]
    public void ClosingAnOccurrence_RequiresCompletedWorkAndEmployeePeriods_AndFailsAtomically(string incomplete)
    {
        var flight = DepartedFlight();
        var rtr = Start(flight);
        var user = Guid.NewGuid();
        var closedWindow = TimeWindow.Create(TestData.Now.AddMinutes(5), TestData.Now.AddMinutes(15)).Value;
        if (incomplete.StartsWith("service", StringComparison.Ordinal))
        {
            var service = flight.AddService(TestData.Service(), null,
                incomplete is "service-window" or "service-employee-window" ? TimeWindow.Create(closedWindow.FromUtc, null).Value : closedWindow,
                null, user, TestData.Now, rtr.Id).Value;
            if (incomplete != "service-employee")
                flight.AddServicePerformer(service.Id, TestData.Staff(),
                    incomplete == "service-employee-window" ? TimeWindow.Create(closedWindow.FromUtc, null).Value : closedWindow,
                    user, TestData.Now, rtr.Id).IsSuccess.ShouldBeTrue();
            if (incomplete == "service-employee-window")
            {
                // Imported legacy work must still block RTR closure, even though new writes reject this state.
                typeof(FlightService).GetProperty(nameof(FlightService.Window))!.SetValue(service, closedWindow);
            }
        }
        if (incomplete.StartsWith("task", StringComparison.Ordinal))
        {
            var task = flight.AddTask(FlightTaskType.Major, null, null,
                incomplete == "task-window" ? TimeWindow.Create(closedWindow.FromUtc, null).Value : closedWindow,
                null, user, TestData.Now, rtr.Id).Value;
            if (incomplete != "task-employee")
                flight.AddTaskPerformer(task.Id, TestData.Staff(),
                    incomplete == "task-employee-window" ? TimeWindow.Create(closedWindow.FromUtc, null).Value : closedWindow,
                    null, user, TestData.Now, rtr.Id).IsSuccess.ShouldBeTrue();
        }
        var previousTime = flight.UpdatedAtUtc;
        var previousUser = rtr.UpdatedByUserId;
        var serviceIds = rtr.Services.Select(item => item.Id).ToArray();
        var taskIds = rtr.Tasks.Select(item => item.Id).ToArray();

        var result = flight.CloseReturnToRamp(rtr.Id, Departure(), Guid.NewGuid(), TestData.Now.AddMinutes(1));

        result.IsFailure.ShouldBeTrue();
        rtr.IsOpen.ShouldBeTrue();
        rtr.DepartureDetails.ShouldBeNull();
        rtr.EndedByUserId.ShouldBeNull();
        rtr.Services.Select(item => item.Id).ShouldBe(serviceIds);
        rtr.Tasks.Select(item => item.Id).ShouldBe(taskIds);
        rtr.UpdatedByUserId.ShouldBe(previousUser);
        flight.UpdatedAtUtc.ShouldBe(previousTime);
        flight.LastReturnToRampSequence.ShouldBe(1);
    }

    [Theory]
    [InlineData("arrival")]
    [InlineData("departure")]
    [InlineData("reversed")]
    public void OccurrenceTimeCorrections_RevalidateRetainedWorkBeforeChangingDetailsSignatureOrActors(string correction)
    {
        var flight = DepartedFlight();
        var rtr = Start(flight);
        var service = AddCompletedService(flight, rtr);
        var signature = Signature();
        flight.CloseReturnToRamp(rtr.Id, Departure(signature: signature), Guid.NewGuid(), TestData.Now).IsSuccess.ShouldBeTrue();
        var arrival = rtr.ArrivalDetails;
        var departure = rtr.DepartureDetails;
        var endedBy = rtr.EndedByUserId;
        var previousTime = flight.UpdatedAtUtc;
        Result result = correction switch
        {
            "arrival" => flight.UpdateReturnToRampArrival(rtr.Id, Arrival(service.Window.FromUtc.AddMinutes(1)), Guid.NewGuid(), TestData.Now.AddMinutes(1)),
            "departure" => flight.CloseReturnToRamp(rtr.Id, Departure(service.Window.ToUtc!.Value.AddMinutes(-1)), Guid.NewGuid(), TestData.Now.AddMinutes(1)),
            _ => flight.CloseReturnToRamp(rtr.Id, Departure(rtr.ArrivalDetails.ArrivedAtUtc.AddMinutes(-1)), Guid.NewGuid(), TestData.Now.AddMinutes(1))
        };

        result.IsFailure.ShouldBeTrue();
        rtr.ArrivalDetails.ShouldBeSameAs(arrival);
        rtr.DepartureDetails.ShouldBeSameAs(departure);
        rtr.DepartureDetails!.CustomerSignature.ShouldBe(signature);
        rtr.EndedByUserId.ShouldBe(endedBy);
        rtr.IsOpen.ShouldBeFalse();
        rtr.Services.ShouldHaveSingleItem().ShouldBeSameAs(service);
        flight.UpdatedAtUtc.ShouldBe(previousTime);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OccurrenceBounds_ApplyToEveryServiceAndTaskWrite(bool taskWork)
    {
        var flight = DepartedFlight();
        var rtr = Start(flight);
        var user = Guid.NewGuid();
        var tooEarly = TimeWindow.Create(TestData.Now.AddMinutes(-1), TestData.Now.AddMinutes(5)).Value;
        Result invalidAdd = taskWork
            ? flight.AddTask(FlightTaskType.Major, null, null, tooEarly, null, user, TestData.Now.AddMinutes(1), rtr.Id)
            : flight.AddService(TestData.Service(), null, tooEarly, null, user, TestData.Now.AddMinutes(1), rtr.Id);

        invalidAdd.IsFailure.ShouldBeTrue();
        rtr.Services.ShouldBeEmpty();
        rtr.Tasks.ShouldBeEmpty();

        var service = taskWork ? null : AddCompletedService(flight, rtr);
        var task = taskWork ? AddCompletedTask(flight, rtr) : null;
        Close(flight, rtr);
        var previousTime = flight.UpdatedAtUtc;
        var tooLate = TimeWindow.Create(TestData.Now.AddMinutes(5), TestData.Now.AddMinutes(31)).Value;
        Result invalidUpdate = taskWork
            ? flight.UpdateTask(task!.Id, FlightTaskType.Minor, null, "NEW", tooLate, "NEW", user, TestData.Now.AddMinutes(1), rtr.Id)
            : flight.UpdateService(service!.Id, TestData.Service(), "NEW", tooLate, "NEW", user, TestData.Now.AddMinutes(1), rtr.Id);

        invalidUpdate.IsFailure.ShouldBeTrue();
        (taskWork ? task!.Window : service!.Window).ToUtc.ShouldBe(TestData.Now.AddMinutes(15));
        (taskWork ? task!.Notes : service!.Notes).ShouldBeNull();
        flight.UpdatedAtUtc.ShouldBe(previousTime);
    }

    [Fact]
    public void ClosedOccurrences_AllowAtomicCompletedActivityAddsAndMetadataEdits_ButKeepTheirLastActivityAndStaff()
    {
        var flight = DepartedFlight();
        var rtr = Start(flight);
        var service = AddCompletedService(flight, rtr);
        Close(flight, rtr);
        var task = AddCompletedTask(flight, rtr);
        var user = Guid.NewGuid();
        var servicePerformer = service.Performers.Single();
        var taskPerformer = task.Performers.Single();
        var attachment = flight.AddTaskAttachment(task.Id, Attachment(), user, TestData.Now, rtr.Id).Value;
        var previousTime = flight.UpdatedAtUtc;
        Result[] failures =
        [
            flight.RemoveServicePerformer(service.Id, servicePerformer.Id, user, TestData.Now.AddMinutes(1), rtr.Id),
            flight.RemoveTaskPerformer(task.Id, taskPerformer.Id, user, TestData.Now.AddMinutes(1), rtr.Id),
            flight.UpdateService(service.Id, service.Service, null, TimeWindow.Create(service.Window.FromUtc, null).Value,
                null, user, TestData.Now.AddMinutes(1), rtr.Id),
            flight.UpdateTaskPerformer(task.Id, taskPerformer.Id, taskPerformer.Employee,
                TimeWindow.Create(taskPerformer.Window.FromUtc, null).Value, null, user, TestData.Now.AddMinutes(1), rtr.Id)
        ];

        failures.ShouldAllBe(result => result.IsFailure);
        service.Performers.ShouldHaveSingleItem().ShouldBeSameAs(servicePerformer);
        task.Performers.ShouldHaveSingleItem().ShouldBeSameAs(taskPerformer);
        service.Window.ToUtc.ShouldNotBeNull();
        taskPerformer.Window.ToUtc.ShouldNotBeNull();
        flight.UpdatedAtUtc.ShouldBe(previousTime);

        flight.UpdateService(service.Id, service.Service, "CORRECTED", service.Window, "Corrected notes", user,
            TestData.Now, rtr.Id).IsSuccess.ShouldBeTrue();
        flight.RemoveService(service.Id, user, TestData.Now, rtr.Id).Value.ShouldBeSameAs(service);
        var lastRemoval = flight.RemoveTask(task.Id, user, TestData.Now.AddMinutes(1), rtr.Id);

        lastRemoval.IsFailure.ShouldBeTrue();
        rtr.Services.ShouldBeEmpty();
        rtr.Tasks.ShouldHaveSingleItem().ShouldBeSameAs(task);
        task.Attachments.ShouldHaveSingleItem().ShouldBeSameAs(attachment);
        rtr.IsOpen.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedOccurrenceActivityAdds_RequireCompleteInitialEmployees_AndDoNotAttachPartialGraphs(bool taskWork)
    {
        var flight = DepartedFlight();
        var rtr = Start(flight);
        var original = AddCompletedService(flight, rtr);
        Close(flight, rtr);
        var user = Guid.NewGuid();
        var window = TimeWindow.Create(TestData.Now.AddMinutes(5), TestData.Now.AddMinutes(15)).Value;
        var employee = TestData.Staff();
        var previousTime = flight.UpdatedAtUtc;
        var previousUser = rtr.UpdatedByUserId;
        Result[] failures = taskWork
            ? [
                flight.AddTask(FlightTaskType.Major, null, null, window, null, user, TestData.Now.AddMinutes(1), rtr.Id),
                flight.AddTask(FlightTaskType.Major, null, null, window, null, user, TestData.Now.AddMinutes(1), rtr.Id,
                    [new FlightTaskPerformerInput(employee, TimeWindow.Create(window.FromUtc, null).Value)]),
                flight.AddTask(FlightTaskType.Major, null, null, window, null, user, TestData.Now.AddMinutes(1), rtr.Id,
                    [new FlightTaskPerformerInput(employee, window), new FlightTaskPerformerInput(employee, window)])
            ]
            : [
                flight.AddService(TestData.Service(), null, window, null, user, TestData.Now.AddMinutes(1), rtr.Id),
                flight.AddService(TestData.Service(), null, window, null, user, TestData.Now.AddMinutes(1), rtr.Id,
                    [new FlightServicePerformerInput(employee, TimeWindow.Create(window.FromUtc, null).Value)]),
                flight.AddService(TestData.Service(), null, window, null, user, TestData.Now.AddMinutes(1), rtr.Id,
                    [new FlightServicePerformerInput(employee, window), new FlightServicePerformerInput(employee, window)])
            ];

        failures.ShouldAllBe(result => result.IsFailure);
        rtr.Services.ShouldHaveSingleItem().ShouldBeSameAs(original);
        rtr.Tasks.ShouldBeEmpty();
        flight.ServiceRecords.ShouldHaveSingleItem().ShouldBeSameAs(original);
        flight.TaskRecords.ShouldBeEmpty();
        rtr.UpdatedByUserId.ShouldBe(previousUser);
        flight.UpdatedAtUtc.ShouldBe(previousTime);
        flight.LastReturnToRampSequence.ShouldBe(1);

        if (taskWork)
            AddCompletedTask(flight, rtr).Performers.ShouldHaveSingleItem();
        else
            AddCompletedService(flight, rtr).Performers.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SharedSignatures_AreClonedAcrossNormalAndRtrDepartures_AndMetadataCorrectionsRemainIndependent(bool changeFileName)
    {
        var flight = ScheduleFlight();
        var sharedSignature = Signature("shared.png");
        var normalDetails = DepartureDetails.Create(TestData.Now.AddHours(1), sharedSignature, TestData.Staff(), null).Value;
        flight.SubmitDeparture(TestData.Staff(), normalDetails, Guid.NewGuid(), TestData.Now).IsSuccess.ShouldBeTrue();
        var flightSignature = flight.DepartureSubmissions.Single().Details.CustomerSignature;
        var first = Start(flight);
        AddCompletedService(flight, first);
        var firstDeparture = Departure(signature: sharedSignature);
        flight.CloseReturnToRamp(first.Id, firstDeparture, Guid.NewGuid(), TestData.Now).IsSuccess.ShouldBeTrue();
        var firstStoredSignature = first.DepartureDetails!.CustomerSignature;

        flightSignature.ShouldBe(sharedSignature);
        flightSignature.ShouldNotBeSameAs(sharedSignature);
        firstStoredSignature.ShouldBe(sharedSignature);
        firstStoredSignature.ShouldNotBeSameAs(sharedSignature);
        firstStoredSignature.ShouldNotBeSameAs(flightSignature);
        flightSignature.FileName.ShouldBe("shared.png");
        flightSignature.SignedAtUtc.ShouldBe(TestData.Now);

        var second = Start(flight);
        AddCompletedTask(flight, second);
        Close(flight, second);
        var replacement = CustomerSignature.Create(changeFileName ? "replacement.png" : sharedSignature.FileName,
            sharedSignature.StorageKey, sharedSignature.ContentType, sharedSignature.SizeBytes,
            changeFileName ? sharedSignature.SignedAtUtc : sharedSignature.SignedAtUtc.AddMinutes(1)).Value;
        var correctedDeparture = RtrDepartureDetails.Create(firstDeparture.DepartedAtUtc, replacement,
            firstDeparture.HandledByEmployee, firstDeparture.Remarks).Value;

        flight.CloseReturnToRamp(first.Id, correctedDeparture, Guid.NewGuid(), TestData.Now.AddMinutes(1))
            .Value.ShouldBeSameAs(first);

        first.DepartureDetails!.CustomerSignature.ShouldBe(replacement);
        first.DepartureDetails.CustomerSignature.ShouldNotBeSameAs(replacement);
        first.DepartureDetails.CustomerSignature.ShouldNotBe(sharedSignature);
        first.UpdatedAtUtc.ShouldBe(TestData.Now.AddMinutes(1));
        second.DepartureDetails!.CustomerSignature.ShouldBeNull();
        flight.DepartureSubmissions.Single().Details.CustomerSignature.ShouldBeSameAs(flightSignature);
        flightSignature.ShouldBe(sharedSignature);

        var equivalentSignature = CustomerSignature.Create($"folder/{replacement.FileName}", replacement.StorageKey,
            replacement.ContentType, replacement.SizeBytes, replacement.SignedAtUtc.ToOffset(TimeSpan.FromHours(3))).Value;
        flight.CloseReturnToRamp(first.Id, RtrDepartureDetails.Create(firstDeparture.DepartedAtUtc, equivalentSignature,
            firstDeparture.HandledByEmployee, firstDeparture.Remarks).Value, Guid.NewGuid(), TestData.Now.AddMinutes(2))
            .IsSuccess.ShouldBeTrue();
        first.UpdatedAtUtc.ShouldBe(TestData.Now.AddMinutes(1));

        flight.CloseReturnToRamp(first.Id, RtrDepartureDetails.Create(firstDeparture.DepartedAtUtc, null,
            firstDeparture.HandledByEmployee, firstDeparture.Remarks).Value, Guid.NewGuid(), TestData.Now.AddMinutes(3))
            .IsSuccess.ShouldBeTrue();

        first.DepartureDetails!.CustomerSignature.ShouldBeNull();
        second.DepartureDetails!.CustomerSignature.ShouldBeNull();
        flight.DepartureSubmissions.Single().Details.CustomerSignature.ShouldBeSameAs(flightSignature);
    }

    [Fact]
    public void RemovingAnOccurrence_RemovesOnlyItsOwnedWorkAndPreservesOtherAndNormalRecords()
    {
        var flight = DepartedFlight();
        var normalService = flight.AddService(TestData.Service(), null, Window(), null, Guid.NewGuid(), TestData.Now).Value;
        var normalTask = flight.AddTask(FlightTaskType.Major, null, null, Window(), null, Guid.NewGuid(), TestData.Now).Value;
        var first = Start(flight);
        var firstService = AddCompletedService(flight, first);
        var firstTask = AddCompletedTask(flight, first);
        var attachment = flight.AddTaskAttachment(firstTask.Id, Attachment(), Guid.NewGuid(), TestData.Now, first.Id).Value;
        Close(flight, first);
        var second = Start(flight);
        var secondService = AddCompletedService(flight, second);

        var removed = flight.RemoveReturnToRamp(first.Id, Guid.NewGuid(), TestData.Now.AddMinutes(1)).Value;

        removed.ShouldBeSameAs(first);
        removed.Services.ShouldHaveSingleItem().ShouldBeSameAs(firstService);
        removed.Tasks.ShouldHaveSingleItem().ShouldBeSameAs(firstTask);
        firstTask.Attachments.ShouldHaveSingleItem().ShouldBeSameAs(attachment);
        flight.ReturnToRamps.ShouldHaveSingleItem().ShouldBeSameAs(second);
        flight.ServiceRecords.Select(item => item.Id).ShouldBe([normalService.Id, secondService.Id]);
        flight.TaskRecords.ShouldHaveSingleItem().ShouldBeSameAs(normalTask);
        flight.Services.ShouldHaveSingleItem().ShouldBeSameAs(normalService);
        flight.Tasks.ShouldHaveSingleItem().ShouldBeSameAs(normalTask);
        flight.LastReturnToRampSequence.ShouldBe(2);
    }

    [Fact]
    public void ForeignOccurrenceIdsAndMissingCorrectionDetails_DoNotChangeExistingWorkOrSequence()
    {
        var flight = DepartedFlight();
        var rtr = Start(flight);
        var foreignId = Start(DepartedFlight()).Id;
        var previousTime = flight.UpdatedAtUtc;
        Result[] failures =
        [
            flight.UpdateReturnToRampArrival(foreignId, Arrival(), Guid.NewGuid(), TestData.Now.AddMinutes(1)),
            flight.CloseReturnToRamp(foreignId, Departure(), Guid.NewGuid(), TestData.Now.AddMinutes(1)),
            flight.RemoveReturnToRamp(foreignId, Guid.NewGuid(), TestData.Now.AddMinutes(1)),
            flight.UpdateReturnToRampArrival(rtr.Id, null, Guid.NewGuid(), TestData.Now.AddMinutes(1)),
            flight.CloseReturnToRamp(rtr.Id, null, Guid.NewGuid(), TestData.Now.AddMinutes(1)),
            flight.RemoveReturnToRamp(rtr.Id, Guid.Empty, TestData.Now.AddMinutes(1))
        ];

        failures.ShouldAllBe(result => result.IsFailure);
        flight.ReturnToRamps.ShouldHaveSingleItem().ShouldBeSameAs(rtr);
        rtr.IsOpen.ShouldBeTrue();
        rtr.DepartureDetails.ShouldBeNull();
        flight.LastReturnToRampSequence.ShouldBe(1);
        flight.UpdatedAtUtc.ShouldBe(previousTime);
    }

    private static Flight ScheduleFlight() =>
        Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(), TestData.FlightNo(),
            TestData.Schedule(), null, [TestData.Service()], [], Guid.NewGuid(), TestData.Now).Value;

    private static Flight DepartedFlight(bool pending = false)
    {
        var flight = ScheduleFlight();
        var details = DepartureDetails.Create(TestData.Now.AddHours(1),
            CustomerSignature.Create("flight.png", "signatures/flight.png", "image/png", 100, TestData.Now).Value, TestData.Staff(), null).Value;
        flight.SubmitDeparture(TestData.Staff(), details, Guid.NewGuid(), TestData.Now).IsSuccess.ShouldBeTrue();
        if (pending)
            flight.SubmitDeparture(TestData.Staff(), details, Guid.NewGuid(), TestData.Now).IsSuccess.ShouldBeTrue();
        return flight;
    }

    private static RtrArrivalDetails Arrival(DateTimeOffset? at = null, string? remarks = null) =>
        RtrArrivalDetails.Create(at ?? TestData.Now, TestData.Staff(), remarks).Value;

    private static RtrDepartureDetails Departure(DateTimeOffset? at = null, CustomerSignature? signature = null) =>
        RtrDepartureDetails.Create(at ?? TestData.Now.AddMinutes(30), signature, TestData.Staff(), null).Value;

    private static CustomerSignature Signature(string fileName = "signature.png") =>
        CustomerSignature.Create(fileName, $"signatures/{fileName}", "image/png", 100, TestData.Now).Value;

    private static FlightReturnToRamp Start(Flight flight) =>
        flight.StartReturnToRamp(Arrival(), Guid.NewGuid(), TestData.Now).Value;

    private static FlightReturnToRamp Close(Flight flight, FlightReturnToRamp rtr) =>
        flight.CloseReturnToRamp(rtr.Id, Departure(rtr.ArrivalDetails.ArrivedAtUtc.AddMinutes(30)), Guid.NewGuid(), TestData.Now).Value;

    private static FlightService AddCompletedService(Flight flight, FlightReturnToRamp rtr)
    {
        var from = rtr.ArrivalDetails.ArrivedAtUtc.AddMinutes(5);
        var window = TimeWindow.Create(from, from.AddMinutes(10)).Value;
        return flight.AddService(TestData.Service(), null, window, null, Guid.NewGuid(), TestData.Now, rtr.Id,
            [new FlightServicePerformerInput(TestData.Staff(), window)]).Value;
    }

    private static FlightTask AddCompletedTask(Flight flight, FlightReturnToRamp rtr)
    {
        var from = rtr.ArrivalDetails.ArrivedAtUtc.AddMinutes(5);
        var window = TimeWindow.Create(from, from.AddMinutes(10)).Value;
        return flight.AddTask(FlightTaskType.Major, new AtaChapterSnapshot(Guid.NewGuid(), "32", "Landing gear"), null,
            window, null, Guid.NewGuid(), TestData.Now, rtr.Id,
            [new FlightTaskPerformerInput(TestData.Staff(), window)]).Value;
    }

    private static TimeWindow Window() => TimeWindow.Create(TestData.Now, null).Value;
    private static ToolSnapshot Tool() => new(Guid.NewGuid(), "Torque wrench");
    private static ResourceUsage Duration(DateTimeOffset from) =>
        ResourceUsage.Create(ResourceCalculationType.Duration, null, from, null).Value;
    private static AttachmentDetails Attachment() =>
        AttachmentDetails.Create("evidence.jpg", "operations/evidence.jpg", "image/jpeg", 200, AttachmentKind.Image).Value;
}
