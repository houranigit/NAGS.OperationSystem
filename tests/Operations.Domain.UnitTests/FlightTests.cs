using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
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

    [Fact]
    public void ScheduleNew_StartsScheduled_AndPreservesOriginalNumber()
    {
        var flight = ScheduleFlight(TestData.Service());

        flight.Status.ShouldBe(FlightStatus.Scheduled);
        flight.OriginalFlightNumber.ShouldBe("SV1020");
        flight.FlightNumber.Value.ShouldBe("SV1020");
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
    public void ChangeFlightNumber_KeepsOriginal_UpdatesCurrent()
    {
        var flight = ScheduleFlight(TestData.Service());

        var change = flight.ChangeFlightNumber(TestData.FlightNo("SV999"), TestData.Now);

        change.IsSuccess.ShouldBeTrue();
        flight.OriginalFlightNumber.ShouldBe("SV1020");
        flight.FlightNumber.Value.ShouldBe("SV999");
    }

    [Fact]
    public void OnWorkOrderOpened_MovesScheduledToInProgress()
    {
        var flight = ScheduleFlight(TestData.Service());
        flight.OnWorkOrderOpened(TestData.Now);
        flight.Status.ShouldBe(FlightStatus.InProgress);
    }

    [Fact]
    public void SettleCompleted_LocksFlight_AndBlocksNumberChange()
    {
        var flight = ScheduleFlight(TestData.Service());
        flight.SettleCompleted(Guid.NewGuid(), TestData.Now);

        flight.Status.ShouldBe(FlightStatus.Completed);
        flight.IsUpdateLocked.ShouldBeTrue();
        flight.ChangeFlightNumber(TestData.FlightNo("SV2"), TestData.Now).IsFailure.ShouldBeTrue();
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
