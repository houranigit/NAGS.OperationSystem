using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class FlightServiceContributionTests
{
    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("save")]
    public void Closing_a_service_with_an_open_performer_rejects_without_mutating_work(string operation)
    {
        var flight = ScheduleFlight();
        var owner = Guid.NewGuid();
        var employee = TestData.Staff();
        var recordId = Guid.NewGuid();
        var performerId = Guid.NewGuid();
        var selection = TestData.Service();
        var started = TimeWindow.Create(TestData.Now, null).Value;
        var closed = TimeWindow.Create(TestData.Now, TestData.Now.AddHours(1)).Value;
        if (operation != "create")
            flight.AddService(selection, "ORIGINAL", started, null, owner, TestData.Now,
                initialPerformers: [new(employee, started, performerId)], id: recordId).IsSuccess.ShouldBeTrue();
        var updatedAt = flight.UpdatedAtUtc;

        var result = operation switch
        {
            "create" => flight.AddService(selection, "INVALID", closed, null, owner, TestData.Now.AddHours(1),
                initialPerformers: [new(employee, started, performerId)], id: recordId),
            "update" => flight.UpdateService(recordId, selection, "INVALID", closed, null, owner, TestData.Now.AddHours(1)),
            _ => flight.SaveService(recordId, selection, "INVALID", closed, null,
                [new(employee, started, performerId)], owner, TestData.Now.AddHours(1))
        };

        result.Error.Code.ShouldBe("Operations.FlightService.OpenPerformerPeriods");
        flight.UpdatedAtUtc.ShouldBe(updatedAt);
        if (operation == "create") flight.Services.ShouldBeEmpty();
        else
        {
            var retained = flight.Services.ShouldHaveSingleItem();
            retained.Window.ShouldBe(started);
            retained.CustomerReferenceNumber.ShouldBe("ORIGINAL");
            retained.Performers.ShouldHaveSingleItem().Window.ShouldBe(started);
        }
    }

    [Fact]
    public void A_closed_service_cannot_add_or_reopen_an_open_performer_until_the_service_is_reopened()
    {
        var flight = ScheduleFlight();
        var owner = Guid.NewGuid();
        var closed = TimeWindow.Create(TestData.Now, TestData.Now.AddHours(1)).Value;
        var started = TimeWindow.Create(TestData.Now, null).Value;
        var service = flight.AddService(TestData.Service(), null, closed, null, owner, TestData.Now,
            initialPerformers: [new(TestData.Staff(), closed)]).Value;
        var performer = service.Performers.Single();

        flight.AddServicePerformer(service.Id, TestData.Staff(), started, owner, TestData.Now.AddHours(2))
            .Error.Code.ShouldBe("Operations.FlightService.OpenPerformerPeriods");
        flight.UpdateServicePerformer(service.Id, performer.Id, performer.Employee, started, owner, TestData.Now.AddHours(2))
            .Error.Code.ShouldBe("Operations.FlightService.OpenPerformerPeriods");
        performer.Window.ShouldBe(closed);
        flight.UpdateService(service.Id, service.Service, null, started, null, owner, TestData.Now.AddHours(2))
            .IsSuccess.ShouldBeTrue();
        flight.UpdateServicePerformer(service.Id, performer.Id, performer.Employee, started, owner, TestData.Now.AddHours(2))
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Another_users_current_end_protects_service_identity_and_removal()
    {
        var flight = ScheduleFlight();
        var owner = Guid.NewGuid();
        var contributor = Guid.NewGuid();
        var open = TimeWindow.Create(TestData.Now, null).Value;
        var closed = TimeWindow.Create(TestData.Now, TestData.Now.AddHours(1)).Value;
        var service = flight.AddService(TestData.Service(), "REF", open, null, owner, TestData.Now).Value;
        flight.UpdateService(service.Id, service.Service, "REF", closed, null, contributor, TestData.Now)
            .IsSuccess.ShouldBeTrue();
        var original = service.Service;
        var before = flight.UpdatedAtUtc;

        flight.UpdateService(service.Id, TestData.Service(), "INVALID", service.Window, null, owner, TestData.Now.AddHours(2))
            .Error.Code.ShouldBe("Operations.FlightService.SelectionReadOnly");
        flight.SaveService(service.Id, TestData.Service(), "INVALID", service.Window, null, [], owner, TestData.Now.AddHours(2))
            .Error.Code.ShouldBe("Operations.FlightService.SelectionReadOnly");
        flight.RemoveService(service.Id, owner, TestData.Now.AddHours(2))
            .Error.Code.ShouldBe("Operations.FlightService.DeleteReadOnly");

        service.HasOtherUserEnd.ShouldBeTrue();
        flight.Services.ShouldHaveSingleItem().ShouldBeSameAs(service);
        service.Service.ShouldBeSameAs(original);
        service.CustomerReferenceNumber.ShouldBe("REF");
        flight.UpdatedAtUtc.ShouldBe(before);
        flight.UpdateService(service.Id, service.Service, "CORRECTED", service.Window, "Own core correction", owner, TestData.Now.AddHours(2))
            .IsSuccess.ShouldBeTrue();
        service.HasOtherUserEnd.ShouldBeTrue();
    }

    private static Flight ScheduleFlight() => Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(),
        TestData.FlightNo(), TestData.Schedule(), null, [TestData.Service()], [], Guid.NewGuid(), TestData.Now).Value;
}
