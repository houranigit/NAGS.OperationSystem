using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class FlightServiceIdentityTests
{
    [Fact]
    public void AddService_PreservesClientParentAndInitialPerformerIdentities()
    {
        var flight = ScheduleFlight();
        var serviceId = Guid.NewGuid();
        var performerId = Guid.NewGuid();
        var employee = TestData.Staff();
        var userId = Guid.NewGuid();

        var result = flight.AddService(TestData.Service(), "REF-1", Window(), "Work started", userId, TestData.Now,
            initialPerformers: [new FlightServicePerformerInput(employee, Window(), performerId)], id: serviceId);

        result.IsSuccess.ShouldBeTrue();
        var service = flight.Services.ShouldHaveSingleItem();
        service.Id.ShouldBe(serviceId);
        service.FlightId.ShouldBe(flight.Id);
        service.CreatedByUserId.ShouldBe(userId);
        var performer = service.Performers.ShouldHaveSingleItem();
        performer.Id.ShouldBe(performerId);
        performer.FlightServiceId.ShouldBe(serviceId);
        performer.Employee.ShouldBe(employee);
    }

    [Fact]
    public void AddServicePerformer_PreservesAClientIdentityOnAnExistingService()
    {
        var flight = ScheduleFlight();
        var service = flight.AddService(TestData.Service(), null, Window(), null, Guid.NewGuid(), TestData.Now).Value;
        var performerId = Guid.NewGuid();

        var added = flight.AddServicePerformer(service.Id, TestData.Staff(), Window(), Guid.NewGuid(),
            TestData.Now.AddMinutes(1), id: performerId);

        added.IsSuccess.ShouldBeTrue();
        service.Performers.ShouldHaveSingleItem().Id.ShouldBe(performerId);
    }

    [Fact]
    public void DistinctClientParentIdentities_AllowRepeatedOccurrencesOfTheSameCatalogService()
    {
        var flight = ScheduleFlight();
        var catalogService = TestData.Service();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        flight.AddService(catalogService, null, Window(), null, userId, TestData.Now, id: firstId)
            .IsSuccess.ShouldBeTrue();
        flight.AddService(catalogService, null, Window(), null, userId, TestData.Now, id: secondId)
            .IsSuccess.ShouldBeTrue();

        flight.Services.Select(item => item.Id).ShouldBe([firstId, secondId]);
        flight.Services.ShouldAllBe(item => item.Service.ServiceId == catalogService.ServiceId);
    }

    [Fact]
    public void DuplicateClientParentIdentity_DoesNotAddWorkOrChangeExistingAuditData()
    {
        var flight = ScheduleFlight();
        var recordId = Guid.NewGuid();
        var service = flight.AddService(TestData.Service(), "ORIGINAL", Window(), "Original notes", Guid.NewGuid(),
            TestData.Now, id: recordId).Value;
        var updatedAt = flight.UpdatedAtUtc;

        var result = flight.AddService(TestData.Service(), "DIFFERENT", Window(), "Different notes", Guid.NewGuid(),
            TestData.Now.AddMinutes(10), id: recordId);

        result.IsFailure.ShouldBeTrue();
        flight.Services.ShouldHaveSingleItem().ShouldBeSameAs(service);
        service.CustomerReferenceNumber.ShouldBe("ORIGINAL");
        service.Notes.ShouldBe("Original notes");
        service.UpdatedAtUtc.ShouldBeNull();
        service.UpdatedByUserId.ShouldBeNull();
        flight.UpdatedAtUtc.ShouldBe(updatedAt);
    }

    [Fact]
    public void DuplicateInitialPerformerIdentity_RejectsTheWholeNewService()
    {
        var flight = ScheduleFlight();
        var performerId = Guid.NewGuid();
        var updatedAt = flight.UpdatedAtUtc;

        var result = flight.AddService(TestData.Service(), null, Window(), null, Guid.NewGuid(),
            TestData.Now.AddMinutes(10), initialPerformers:
            [
                new FlightServicePerformerInput(TestData.Staff(), Window(), performerId),
                new FlightServicePerformerInput(TestData.Staff(), Window(), performerId)
            ], id: Guid.NewGuid());

        result.IsFailure.ShouldBeTrue();
        flight.Services.ShouldBeEmpty();
        flight.UpdatedAtUtc.ShouldBe(updatedAt);
    }

    [Fact]
    public void DuplicatePerformerIdentity_DoesNotReplaceAnotherEmployeeOrChangeAuditData()
    {
        var flight = ScheduleFlight();
        var service = flight.AddService(TestData.Service(), null, Window(), null, Guid.NewGuid(), TestData.Now).Value;
        var performerId = Guid.NewGuid();
        var employee = TestData.Staff();
        var performer = flight.AddServicePerformer(service.Id, employee, Window(), Guid.NewGuid(),
            TestData.Now.AddMinutes(1), id: performerId).Value;
        var updatedAt = service.UpdatedAtUtc;
        var updatedBy = service.UpdatedByUserId;
        var flightUpdatedAt = flight.UpdatedAtUtc;

        var result = flight.AddServicePerformer(service.Id, TestData.Staff(), Window(), Guid.NewGuid(),
            TestData.Now.AddMinutes(10), id: performerId);

        result.IsFailure.ShouldBeTrue();
        service.Performers.ShouldHaveSingleItem().ShouldBeSameAs(performer);
        performer.Employee.ShouldBe(employee);
        service.UpdatedAtUtc.ShouldBe(updatedAt);
        service.UpdatedByUserId.ShouldBe(updatedBy);
        flight.UpdatedAtUtc.ShouldBe(flightUpdatedAt);
    }

    [Fact]
    public void SaveService_AtomicallyMovesParentAndPerformerStarts_WhilePreservingTheirIdsAndAttachments()
    {
        var flight = ScheduleFlight();
        var serviceId = Guid.NewGuid();
        var performerId = Guid.NewGuid();
        var employee = TestData.Staff();
        var creator = Guid.NewGuid();
        var service = flight.AddService(TestData.Service(), "REF-ORIGINAL", Window(), "Original notes", creator, TestData.Now,
            initialPerformers: [new(employee, Window(), performerId)], id: serviceId).Value;
        var attachment = flight.AddServiceAttachment(serviceId,
            AttachmentDetails.Create("photo.jpg", "operations/photo.jpg", "image/jpeg", 10, AttachmentKind.Image).Value,
            creator, TestData.Now).Value;
        var performer = service.Performers.ShouldHaveSingleItem();
        var nextWindow = TimeWindow.Create(TestData.Now.AddHours(1), null).Value;
        var editor = Guid.NewGuid();
        var editedAt = TestData.Now.AddHours(2);

        var result = flight.SaveService(serviceId, service.Service, "REF-UPDATED", nextWindow, "Updated notes",
            [new(employee, nextWindow, performerId)], editor, editedAt);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(service);
        service.Id.ShouldBe(serviceId);
        service.Window.ShouldBe(nextWindow);
        service.Performers.ShouldHaveSingleItem().ShouldBeSameAs(performer);
        performer.Id.ShouldBe(performerId);
        performer.Window.ShouldBe(nextWindow);
        service.Attachments.ShouldHaveSingleItem().ShouldBeSameAs(attachment);
        service.CreatedByUserId.ShouldBe(creator);
        service.CreatedAtUtc.ShouldBe(TestData.Now);
        service.UpdatedByUserId.ShouldBe(editor);
        service.UpdatedAtUtc.ShouldBe(editedAt);
        flight.UpdatedAtUtc.ShouldBe(editedAt);
    }

    [Fact]
    public void SaveService_InvalidProspectivePerformerWindow_DoesNotPartiallyChangeTheRetainedGraphOrAuditData()
    {
        var flight = ScheduleFlight();
        var serviceId = Guid.NewGuid();
        var performerId = Guid.NewGuid();
        var employee = TestData.Staff();
        var creator = Guid.NewGuid();
        var service = flight.AddService(TestData.Service(), "REF-ORIGINAL", Window(), "Original notes", creator, TestData.Now,
            initialPerformers: [new(employee, Window(), performerId)], id: serviceId).Value;
        var attachment = flight.AddServiceAttachment(serviceId,
            AttachmentDetails.Create("photo.jpg", "operations/photo.jpg", "image/jpeg", 10, AttachmentKind.Image).Value,
            creator, TestData.Now.AddMinutes(1)).Value;
        var performer = service.Performers.ShouldHaveSingleItem();
        var originalService = service.Service;
        var originalWindow = service.Window;
        var originalEmployee = performer.Employee;
        var originalPeriod = performer.Window;
        var updatedAt = service.UpdatedAtUtc;
        var updatedBy = service.UpdatedByUserId;
        var flightUpdatedAt = flight.UpdatedAtUtc;
        var nextWindow = TimeWindow.Create(TestData.Now.AddHours(1), null).Value;

        var result = flight.SaveService(serviceId, TestData.Service(), "MUST-NOT-LEAK", nextWindow, "Must not leak",
            [new(TestData.Staff(), originalPeriod, performerId)], Guid.NewGuid(), TestData.Now.AddHours(2));

        result.IsFailure.ShouldBeTrue();
        flight.Services.ShouldHaveSingleItem().ShouldBeSameAs(service);
        service.Id.ShouldBe(serviceId);
        service.Service.ShouldBeSameAs(originalService);
        service.Window.ShouldBeSameAs(originalWindow);
        service.CustomerReferenceNumber.ShouldBe("REF-ORIGINAL");
        service.Notes.ShouldBe("Original notes");
        service.Performers.ShouldHaveSingleItem().ShouldBeSameAs(performer);
        performer.Id.ShouldBe(performerId);
        performer.Employee.ShouldBeSameAs(originalEmployee);
        performer.Window.ShouldBeSameAs(originalPeriod);
        service.Attachments.ShouldHaveSingleItem().ShouldBeSameAs(attachment);
        service.CreatedByUserId.ShouldBe(creator);
        service.UpdatedAtUtc.ShouldBe(updatedAt);
        service.UpdatedByUserId.ShouldBe(updatedBy);
        flight.UpdatedAtUtc.ShouldBe(flightUpdatedAt);
    }

    [Theory]
    [InlineData("service")]
    [InlineData("initial-performer")]
    [InlineData("later-performer")]
    public void ExplicitEmptyClientIdentity_IsRejectedWithoutMutatingWork(string target)
    {
        var flight = ScheduleFlight();
        if (target == "later-performer")
        {
            var service = flight.AddService(TestData.Service(), null, Window(), null, Guid.NewGuid(), TestData.Now).Value;
            var updatedAt = flight.UpdatedAtUtc;

            var result = flight.AddServicePerformer(service.Id, TestData.Staff(), Window(), Guid.NewGuid(),
                TestData.Now.AddMinutes(10), id: Guid.Empty);

            result.IsFailure.ShouldBeTrue();
            service.Performers.ShouldBeEmpty();
            service.UpdatedAtUtc.ShouldBeNull();
            flight.UpdatedAtUtc.ShouldBe(updatedAt);
            return;
        }

        var before = flight.UpdatedAtUtc;
        var added = flight.AddService(TestData.Service(), null, Window(), null, Guid.NewGuid(),
            TestData.Now.AddMinutes(10),
            initialPerformers: target == "initial-performer"
                ? [new FlightServicePerformerInput(TestData.Staff(), Window(), Guid.Empty)]
                : null,
            id: target == "service" ? Guid.Empty : Guid.NewGuid());

        added.IsFailure.ShouldBeTrue();
        flight.Services.ShouldBeEmpty();
        flight.UpdatedAtUtc.ShouldBe(before);
    }

    private static Flight ScheduleFlight() =>
        Flight.ScheduleNew(TestData.Customer(), TestData.Station(), TestData.OperationType(), TestData.FlightNo(),
            TestData.Schedule(), null, [TestData.Service()], [], Guid.NewGuid(), TestData.Now).Value;

    private static TimeWindow Window() => TimeWindow.Create(TestData.Now, null).Value;
}
