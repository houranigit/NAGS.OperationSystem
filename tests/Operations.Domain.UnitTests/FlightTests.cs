using Operations.Domain.Enumerations;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class FlightTests
{
    private static Operations.Domain.Flights.Flight ScheduleFlight(params ServiceSnapshot[] planned)
    {
        var result = Operations.Domain.Flights.Flight.ScheduleNew(
            TestData.Customer(), TestData.Station(), TestData.OperationType(), TestData.FlightNo(),
            TestData.Schedule(), aircraftType: null, planned, assignedEmployees: [],
            contractId: null, contractNumber: null, createdByUserId: Guid.NewGuid(), now: TestData.Now);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private static WorkOrder ApprovedCompletionFor(Operations.Domain.Flights.Flight flight, int sequence = 1)
    {
        var context = new FlightContext(flight.Id, flight.Customer, flight.Station, flight.OperationType,
            flight.FlightNumber, flight.Schedule, flight.AircraftType);
        var workOrder = WorkOrder.OpenCompletion(context, Guid.NewGuid(), TestData.Staff(), TestData.Now);
        workOrder.SetActualAircraftType(new AircraftTypeSnapshot(Guid.NewGuid(), "Airbus", "A320"), TestData.Now);
        workOrder.SetActualFlightNumber(TestData.FlightNo("SV777"), TestData.Now);
        workOrder.SetAircraftTailNumber("HZ-AK11", TestData.Now);
        workOrder.SetActualTimes(ActualTime.Create(TestData.Now, TestData.Now.AddHours(1)).Value, TestData.Now);
        workOrder.SetRemarks("All good", TestData.Now);
        workOrder.Submit(TestData.Now);
        workOrder.Approve(WorkOrderNumber.FromStationSequence("RUH", sequence), Guid.NewGuid(), TestData.Now).IsSuccess.ShouldBeTrue();
        return workOrder;
    }

    private static WorkOrder ApprovedCancellationFor(Operations.Domain.Flights.Flight flight, int sequence = 1)
    {
        var context = new FlightContext(flight.Id, flight.Customer, flight.Station, flight.OperationType,
            flight.FlightNumber, flight.Schedule, flight.AircraftType);
        var cancellation = new CancellationDetails(Guid.NewGuid(), TestData.Now, "Customer canceled");
        var workOrder = WorkOrder.OpenCancellation(context, cancellation, Guid.NewGuid(), TestData.Staff(), TestData.Now);
        workOrder.Submit(TestData.Now);
        workOrder.Approve(WorkOrderNumber.FromStationSequence("RUH", sequence), Guid.NewGuid(), TestData.Now).IsSuccess.ShouldBeTrue();
        return workOrder;
    }

    [Fact]
    public void ScheduleNew_StartsScheduled_AndPreservesOriginalNumber()
    {
        var flight = ScheduleFlight(TestData.Service());

        flight.Status.ShouldBe(FlightStatus.Scheduled);
        flight.OriginalFlightNumber.ShouldBe("SV1020");
        flight.FlightNumber.Value.ShouldBe("SV1020");
    }

    [Fact]
    public void ScheduleNew_WithoutPlannedServices_Fails()
    {
        var result = Operations.Domain.Flights.Flight.ScheduleNew(
            TestData.Customer(), TestData.Station(), TestData.OperationType(), TestData.FlightNo(),
            TestData.Schedule(), null, [], [],
            null, null, Guid.NewGuid(), TestData.Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.PlannedServices.Required");
    }

    [Fact]
    public void ScheduleNew_WithPerLandingMix_Fails()
    {
        var result = Operations.Domain.Flights.Flight.ScheduleNew(
            TestData.Customer(), TestData.Station(), TestData.OperationType(), TestData.FlightNo(),
            TestData.Schedule(), null, [TestData.PerLandingService(), TestData.Service()], [],
            null, null, Guid.NewGuid(), TestData.Now);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void CreateAdHoc_WithoutPlannedServices_Fails_UnlessCancellation()
    {
        var withoutServices = Operations.Domain.Flights.Flight.CreateAdHoc(
            TestData.Customer(), TestData.Station(), TestData.OperationType(), TestData.FlightNo(),
            TestData.Schedule(), null, [], TestData.Staff(), Guid.NewGuid(), TestData.Now);
        withoutServices.IsFailure.ShouldBeTrue();
        withoutServices.Error.Code.ShouldBe("Operations.PlannedServices.Required");

        var cancellationException = Operations.Domain.Flights.Flight.CreateAdHoc(
            TestData.Customer(), TestData.Station(), TestData.OperationType(), TestData.FlightNo(),
            TestData.Schedule(), null, [], TestData.Staff(), Guid.NewGuid(), TestData.Now,
            allowEmptyPlannedServices: true);
        cancellationException.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ChangeFlightNumber_KeepsOriginal_UpdatesCurrent()
    {
        var flight = ScheduleFlight(TestData.Service());

        var change = flight.ChangeFlightNumber(TestData.FlightNo("SV999"), TestData.Now);

        change.IsSuccess.ShouldBeTrue();
        flight.OriginalFlightNumber.ShouldBe("SV1020");
        flight.FlightNumber.Value.ShouldBe("SV999");
    }

    [Fact]
    public void OnWorkOrderSubmitted_MovesScheduledToInProgress()
    {
        // Opening a draft work order does not change the flight; only submission does.
        var flight = ScheduleFlight(TestData.Service());

        flight.OnWorkOrderSubmitted(TestData.Now);

        flight.Status.ShouldBe(FlightStatus.InProgress);
    }

    [Fact]
    public void SettleCompleted_CapturesApprovedValues_AndLocksFlight()
    {
        var flight = ScheduleFlight(TestData.Service());
        var workOrder = ApprovedCompletionFor(flight);

        var settle = flight.SettleCompleted(workOrder, TestData.Now);

        settle.IsSuccess.ShouldBeTrue();
        flight.Status.ShouldBe(FlightStatus.Completed);
        flight.IsUpdateLocked.ShouldBeTrue();
        flight.ApprovedWorkOrder.ShouldNotBeNull();
        flight.ApprovedWorkOrder!.WorkOrderId.ShouldBe(workOrder.Id);
        flight.ApprovedWorkOrder.WorkOrderNumber.ShouldBe("RUH-0001");
        flight.ApprovedWorkOrder.ActualFlightNumber.ShouldBe("SV777");
        flight.ApprovedWorkOrder.ActualAircraftTypeModel.ShouldBe("A320");
        flight.ApprovedWorkOrder.AircraftTailNumber.ShouldBe("HZ-AK11");
        flight.ApprovedWorkOrder.ActualArrivalUtc.ShouldNotBeNull();
        flight.ApprovedWorkOrder.ActualDepartureUtc.ShouldNotBeNull();
        flight.ApprovedWorkOrder.Remarks.ShouldBe("All good");
        flight.ChangeFlightNumber(TestData.FlightNo("SV2"), TestData.Now).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void SettleCanceled_CapturesCancellationValues()
    {
        var flight = ScheduleFlight(TestData.Service());
        var workOrder = ApprovedCancellationFor(flight);

        var settle = flight.SettleCanceled(workOrder, TestData.Now);

        settle.IsSuccess.ShouldBeTrue();
        flight.Status.ShouldBe(FlightStatus.Canceled);
        flight.ApprovedWorkOrder.ShouldNotBeNull();
        flight.ApprovedWorkOrder!.CanceledAtUtc.ShouldNotBeNull();
        flight.ApprovedWorkOrder.CancellationReason.ShouldBe("Customer canceled");
    }

    [Fact]
    public void Settle_WithWrongOutcomeType_Fails()
    {
        var flight = ScheduleFlight(TestData.Service());
        var completion = ApprovedCompletionFor(flight);
        var flight2 = ScheduleFlight(TestData.Service());
        var cancellation = ApprovedCancellationFor(flight2);

        flight.SettleCanceled(completion, TestData.Now).IsFailure.ShouldBeTrue();
        flight2.SettleCompleted(cancellation, TestData.Now).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Settle_WithForeignOrUnapprovedWorkOrder_Fails()
    {
        var flight = ScheduleFlight(TestData.Service());
        var otherFlight = ScheduleFlight(TestData.Service());
        var foreign = ApprovedCompletionFor(otherFlight);

        flight.SettleCompleted(foreign, TestData.Now).IsFailure.ShouldBeTrue();

        var context = new FlightContext(flight.Id, flight.Customer, flight.Station, flight.OperationType,
            flight.FlightNumber, flight.Schedule, flight.AircraftType);
        var draft = WorkOrder.OpenCompletion(context, Guid.NewGuid(), TestData.Staff(), TestData.Now);
        flight.SettleCompleted(draft, TestData.Now).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void ClearApprovedSnapshot_RevertsToInProgress_AndClearsCapturedValues()
    {
        var flight = ScheduleFlight(TestData.Service());
        var workOrder = ApprovedCompletionFor(flight);
        flight.SettleCompleted(workOrder, TestData.Now);

        var clear = flight.ClearApprovedSnapshot(TestData.Now);

        clear.IsSuccess.ShouldBeTrue();
        flight.Status.ShouldBe(FlightStatus.InProgress);
        flight.ApprovedWorkOrder.ShouldBeNull();
        flight.IsUpdateLocked.ShouldBeFalse();
    }

    [Fact]
    public void ClearApprovedSnapshot_WithoutSnapshot_Fails()
    {
        var flight = ScheduleFlight(TestData.Service());
        flight.ClearApprovedSnapshot(TestData.Now).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void PerLandingFlight_IsPerLanding_True()
    {
        var flight = ScheduleFlight(TestData.PerLandingService());
        flight.IsPerLanding.ShouldBeTrue();
    }

    [Fact]
    public void AssignEmployees_IsIdempotentPerStaffMember()
    {
        var flight = ScheduleFlight(TestData.Service());
        var staff = TestData.Staff();

        flight.AssignEmployees([staff], TestData.Now);
        flight.AssignEmployees([staff], TestData.Now);

        flight.AssignedEmployees.Count.ShouldBe(1);
    }
}
