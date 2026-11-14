using MasterData.Contracts.Seeding;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class FlightTests
{
    private static Flight ScheduleFlight(params ServiceSnapshot[] services) =>
        Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(), TestData.FlightNo(),
            TestData.Schedule(), null, services, [], Guid.NewGuid(), TestData.Now).Value;

    [Fact]
    public void ScheduleNew_StoresPlanningListsDirectly_WithoutDuplicates()
    {
        var service = TestData.Service();
        var employee = TestData.Staff();
        var flight = Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(), TestData.FlightNo(),
            TestData.Schedule(), TestData.AircraftType(), [service, service], [employee, employee], Guid.NewGuid(), TestData.Now).Value;

        flight.FlightNumber.Value.ShouldBe("SV1020");
        flight.PlannedServices.ShouldHaveSingleItem().Service.ShouldBe(service);
        flight.AssignedEmployees.ShouldHaveSingleItem().Employee.ShouldBe(employee);
        flight.AircraftType.ShouldNotBeNull();
        flight.CreatedAtUtc.ShouldBe(TestData.Now);
    }

    [Fact]
    public void ScheduleNew_WithoutServices_IsRejected()
    {
        var result = Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(), TestData.FlightNo(),
            TestData.Schedule(), null, [], [], Guid.NewGuid(), TestData.Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.PlannedServices.Required");
    }

    [Fact]
    public void FlightNumberAndSchedule_CanBeEdited()
    {
        var flight = ScheduleFlight(TestData.Service());
        var schedule = ScheduledTime.Create(TestData.Now.AddHours(2), TestData.Now.AddHours(3)).Value;

        flight.ChangeFlightNumber(TestData.FlightNo("SV999"), TestData.Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        flight.UpdatePlanning(schedule, null, flight.PlannedServices.Select(service => service.Service).ToList(), null, TestData.Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();

        flight.FlightNumber.Value.ShouldBe("SV999");
        flight.Schedule.ShouldBe(schedule);
        flight.UpdatedAtUtc.ShouldBe(TestData.Now.AddMinutes(2));
    }

    [Theory]
    [InlineData(null, "123", "Operations.FlightNumber.CustomerPrefixRequired")]
    [InlineData("XY", "XY123", "Operations.FlightNumber.InvalidFormat")]
    [InlineData("5Y", "5Y123/12", "Operations.FlightNumber.InvalidFormat")]
    public void ScheduleNew_RequiresTheFlightCustomersCode(string? numberCustomerCode, string number, string errorCode)
    {
        var flightNumber = FlightNumber.Create(number, numberCustomerCode).Value;
        var result = Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(), flightNumber,
            TestData.Schedule(), null, [TestData.Service()], [], Guid.NewGuid(), TestData.Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(errorCode);
    }

    [Theory]
    [InlineData(" sv ", "sv123", "SV123")]
    [InlineData("5Y", "5y123/12", "5Y123/12")]
    [InlineData(null, "123/1", "123/1")]
    [InlineData("", "123", "123")]
    [InlineData("  ", "123", "123")]
    public void ScheduleNew_AcceptsNumbersForItsCustomer(string? iataCode, string number, string expected)
    {
        var result = Flight.ScheduleNew(new CustomerSnapshot(Guid.NewGuid(), iataCode, "Customer"),
            TestData.Station(), TestData.OperationType(), FlightNumber.Create(number, iataCode).Value,
            TestData.Schedule(), null, [TestData.Service()], [], Guid.NewGuid(), TestData.Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FlightNumber.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, "123", "Operations.FlightNumber.CustomerPrefixRequired")]
    [InlineData("XY", "XY123", "Operations.FlightNumber.InvalidFormat")]
    public void ChangeFlightNumber_RejectsAnotherCustomersNumber_WithoutMutation(string? numberCustomerCode, string number, string errorCode)
    {
        var flight = ScheduleFlight(TestData.Service());
        var newNumber = FlightNumber.Create(number, numberCustomerCode).Value;

        var result = flight.ChangeFlightNumber(newNumber, TestData.Now.AddMinutes(1));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(errorCode);
        flight.FlightNumber.Value.ShouldBe("SV1020");
        flight.UpdatedAtUtc.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ChangeFlightNumber_WithoutCustomerIataCode_DoesNotRequirePrefix(string? iataCode)
    {
        var flight = Flight.ScheduleNew(new CustomerSnapshot(Guid.NewGuid(), iataCode, "Customer"),
            TestData.Station(), TestData.OperationType(), FlightNumber.Create("123").Value,
            TestData.Schedule(), null, [TestData.Service()], [], Guid.NewGuid(), TestData.Now).Value;

        var result = flight.ChangeFlightNumber(FlightNumber.Create("456").Value, TestData.Now.AddMinutes(1));

        result.IsSuccess.ShouldBeTrue();
        flight.FlightNumber.Value.ShouldBe("456");
    }

    [Fact]
    public void ReplacingLists_ReplacesPlanning_AndDeduplicatesStaff()
    {
        var flight = ScheduleFlight(TestData.Service());
        var service = TestData.Service();
        var employee = TestData.Staff();

        flight.UpdatePlanning(flight.Schedule, flight.AircraftType, [service], [employee, employee], TestData.Now).IsSuccess.ShouldBeTrue();

        flight.PlannedServices.ShouldHaveSingleItem().Service.ShouldBe(service);
        flight.AssignedEmployees.ShouldHaveSingleItem().Employee.ShouldBe(employee);
    }

    [Fact]
    public void InvalidPlanningUpdate_LeavesScheduleServicesAndRosterIntact()
    {
        var service = TestData.Service();
        var staff = TestData.Staff();
        var flight = Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(), TestData.FlightNo(),
            TestData.Schedule(), null, [service], [staff], Guid.NewGuid(), TestData.Now).Value;
        var schedule = ScheduledTime.Create(TestData.Now.AddHours(2), TestData.Now.AddHours(3)).Value;

        var result = flight.UpdatePlanning(schedule, TestData.AircraftType(), [TestData.PerLandingService()], null, TestData.Now.AddMinutes(1));

        result.Error.Code.ShouldBe("Operations.PerLanding.AssignmentNotAllowed");
        flight.Schedule.Sta.ShouldBe(TestData.Now);
        flight.AircraftType.ShouldBeNull();
        flight.PlannedServices.ShouldHaveSingleItem().Service.ShouldBe(service);
        flight.AssignedEmployees.ShouldHaveSingleItem().Employee.ShouldBe(staff);
        flight.UpdatedAtUtc.ShouldBeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void AdHoc_RejectsRosterChangesWithoutRelyingOnCreationFingerprint(bool withFingerprint, bool existingRoster)
    {
        var operation = new OperationTypeSnapshot(WellKnownMasterDataIds.AdHocOperationType, "AdHoc");
        var original = TestData.Staff();
        var flight = withFingerprint
            ? Flight.CreateAdHoc(TestData.Customer(), TestData.Station(), operation, TestData.FlightNo(),
                TestData.Schedule(), Guid.NewGuid(), TestData.Now, Guid.NewGuid(), new string('A', 64)).Value
            : Flight.ScheduleNew(TestData.Customer(), TestData.Station(), operation, TestData.FlightNo(),
                TestData.Schedule(), null, [TestData.Service()], existingRoster ? [original] : [], Guid.NewGuid(), TestData.Now).Value;
        var employee = TestData.Staff();
        var roster = flight.AssignedEmployees.ToArray();

        flight.ReplaceAssignedEmployees([employee], TestData.Now.AddMinutes(1)).Error.Code.ShouldBe("Operations.AdHoc.AssignmentsNotAllowed");
        flight.InviteEmployees([employee], TestData.Now.AddMinutes(1)).Error.Code.ShouldBe("Operations.AdHoc.AssignmentsNotAllowed");

        flight.AssignedEmployees.ShouldBe(roster);
        flight.UpdatedAtUtc.ShouldBeNull();
        flight.CanCorrectOperationalWork(employee.StaffMemberId).ShouldBeTrue();
    }

    [Fact]
    public void PerLanding_CannotHaveAssignedStaff_OrBeMixedWithOtherServices()
    {
        var flight = ScheduleFlight(TestData.PerLandingService());

        flight.IsPerLanding.ShouldBeTrue();
        flight.ReplaceAssignedEmployees([TestData.Staff()], TestData.Now).Error.Code.ShouldBe("Operations.PerLanding.AssignmentNotAllowed");
        flight.UpdatePlanning(flight.Schedule, flight.AircraftType, [TestData.PerLandingService(), TestData.Service()], null, TestData.Now).Error.Code.ShouldBe("Operations.PerLanding.NoMix");
        flight.PlannedServices.ShouldHaveSingleItem().IsAircraftPerLanding.ShouldBeTrue();
        flight.AssignedEmployees.ShouldBeEmpty();
    }
}
