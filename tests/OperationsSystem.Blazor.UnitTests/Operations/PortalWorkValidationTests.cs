using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Features.Operations.Components;
using OperationsSystem.Blazor.Client.State;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Operations;

public sealed class PortalWorkValidationTests
{
    private static readonly StationTimeZone Zone = StationTimeZone.FromId("UTC");
    private static readonly DateTimeOffset Hour = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid ServiceId = Guid.NewGuid();

    [Fact]
    public void Same_service_periods_allow_adjacency_and_distinct_zero_duration_but_reject_duplicates_and_overlap()
    {
        var first = Service(Hour, Hour.AddHours(1));
        var adjacent = Service(Hour.AddHours(1), Hour.AddHours(2));
        var moment = Service(Hour.AddMinutes(30), Hour.AddMinutes(30));
        var otherService = Service(Hour, Hour.AddHours(1), Guid.NewGuid());
        var errors = new PortalFlightValidation();

        PortalWorkDraft.ValidateServicePeriods([first, adjacent, moment, otherService], errors);
        errors.Count.ShouldBe(0);

        var duplicateMoment = Service(Hour.AddMinutes(30), Hour.AddMinutes(30));
        var overlapping = Service(Hour.AddMinutes(45), null);
        PortalWorkDraft.ValidateServicePeriods([first, adjacent, moment, duplicateMoment, overlapping, otherService], errors);

        errors.HasError(PortalFlightValidation.Field(duplicateMoment.Id, "start")).ShouldBeTrue();
        errors.HasError(PortalFlightValidation.Field(overlapping.Id, "start")).ShouldBeTrue();
        errors.HasErrorFor(otherService.Id).ShouldBeFalse();
    }

    [Fact]
    public void Same_service_period_validation_includes_retained_work_when_a_record_changes()
    {
        var first = PortalWorkDraft.From(RecordedService(Hour, Hour.AddHours(1)), Zone);
        var second = PortalWorkDraft.From(RecordedService(Hour.AddMinutes(30), Hour.AddHours(2)), Zone);
        var errors = new PortalFlightValidation();

        PortalWorkDraft.ValidateServicePeriods([first, second], errors);
        errors.Count.ShouldBe(0);

        second.Notes = "Updated.";
        PortalWorkDraft.ValidateServicePeriods([first, second], errors);

        errors.HasErrorFor(first.Id).ShouldBeFalse();
        errors.HasError(PortalFlightValidation.Field(second.Id, "start")).ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Performer_fields_report_overlap_invalid_bounds_and_open_periods(bool isTask)
    {
        var work = new PortalWorkDraft(Guid.NewGuid(), isTask, Zone, Hour, Hour.AddHours(1)) { ServiceId = ServiceId };
        var staffId = Guid.NewGuid();
        var first = Performer(staffId, Hour.AddMinutes(5), Hour.AddMinutes(30));
        var overlapping = Performer(staffId, Hour.AddMinutes(20), Hour.AddMinutes(45));
        var early = Performer(Guid.NewGuid(), Hour.AddMinutes(-15), Hour);
        var late = Performer(Guid.NewGuid(), Hour.AddMinutes(30), Hour.AddHours(2));
        var active = Performer(Guid.NewGuid(), Hour.AddMinutes(10), null);
        work.Performers.AddRange([first, overlapping, early, late, active]);
        var errors = new PortalFlightValidation();

        if (isTask)
            work.ToTaskChange(1, errors);
        else
            work.ToServiceChange(1, errors);

        errors.HasError(PortalFlightValidation.Field(overlapping.Id, "start")).ShouldBeTrue();
        errors.HasError(PortalFlightValidation.Field(early.Id, "start")).ShouldBeTrue();
        errors.HasError(PortalFlightValidation.Field(late.Id, "end")).ShouldBeTrue();
        errors.HasError(PortalFlightValidation.Field(active.Id, "end")).ShouldBeTrue();
        errors.HasError(PortalFlightValidation.Field(work.Id, "end")).ShouldBeTrue();
        errors.HasErrorFor(first.Id).ShouldBeFalse();
    }

    [Fact]
    public void Task_duration_resources_must_fit_and_close_while_quantity_resources_need_no_period()
    {
        var task = new PortalWorkDraft(Guid.NewGuid(), true, Zone, Hour, Hour.AddHours(1));
        task.Performers.Add(Performer(Guid.NewGuid(), Hour, Hour.AddHours(1)));
        var earlyTool = Resource(ResourceCalculationType.Duration, Hour.AddMinutes(-5), Hour);
        var openMaterial = Resource(ResourceCalculationType.Duration, Hour, null);
        var lateSupport = Resource(ResourceCalculationType.Duration, Hour, Hour.AddHours(2));
        var quantity = Resource(ResourceCalculationType.Quantity, null, null);
        task.Tools.Add(earlyTool);
        task.Materials.Add(openMaterial);
        task.GeneralSupports.AddRange([lateSupport, quantity]);
        var errors = new PortalFlightValidation();

        task.ToTaskChange(1, errors);

        errors.HasError(PortalFlightValidation.Field(earlyTool.Id, "start")).ShouldBeTrue();
        errors.HasError(PortalFlightValidation.Field(openMaterial.Id, "end")).ShouldBeTrue();
        errors.HasError(PortalFlightValidation.Field(lateSupport.Id, "end")).ShouldBeTrue();
        errors.HasError(PortalFlightValidation.Field(task.Id, "end")).ShouldBeTrue();
        errors.HasErrorFor(quantity.Id).ShouldBeFalse();
    }

    [Fact]
    public void Departure_validation_ignores_unstarted_and_removed_work_and_detects_open_duration_resources()
    {
        var unstarted = Service(null, null);
        var removed = Service(Hour, null);
        removed.Deleted = true;
        var task = new PortalWorkDraft(Guid.NewGuid(), true, Zone, Hour, Hour.AddHours(1));
        task.Performers.Add(Performer(Guid.NewGuid(), Hour, Hour.AddHours(1)));
        var errors = new PortalFlightValidation();
        const string field = "departure-time";

        PortalWorkDraft.ValidateDeparturePeriods([unstarted, removed], [task], errors, field);
        errors.Count.ShouldBe(0);

        task.Tools.Add(Resource(ResourceCalculationType.Duration, Hour, null));
        PortalWorkDraft.ValidateDeparturePeriods([unstarted, removed], [task], errors, field);

        errors.HasError(field).ShouldBeTrue();
        errors.Messages.Single().ShouldContain("Task 1");
    }

    [Fact]
    public void Actual_bounds_report_periods_on_retained_records_when_actual_time_changes()
    {
        var existing = PortalWorkDraft.From(RecordedService(Hour, Hour.AddHours(1)), Zone);
        var errors = new PortalFlightValidation();

        PortalWorkDraft.ValidateActualBounds([existing], errors, Hour.AddMinutes(15), Hour.AddMinutes(45));
        errors.Count.ShouldBe(0);

        PortalWorkDraft.ValidateActualBounds([existing], errors, Hour.AddMinutes(15), Hour.AddMinutes(45), includeUnchanged: true);
        errors.HasError(PortalFlightValidation.Field(existing.Id, "start")).ShouldBeTrue();
        errors.HasError(PortalFlightValidation.Field(existing.Id, "end")).ShouldBeTrue();
    }

    private static PortalWorkDraft Service(DateTimeOffset? start, DateTimeOffset? end, Guid? serviceId = null) =>
        new(Guid.NewGuid(), false, Zone, start, end) { ServiceId = serviceId ?? ServiceId };

    private static PortalFlightService RecordedService(DateTimeOffset start, DateTimeOffset end) =>
        new(Guid.NewGuid(), ServiceId, "Service", start, end, null, null, [], [],
            true, true, true, true, null, null, Guid.NewGuid(), false);

    private static PortalPerformerDraft Performer(Guid staffId, DateTimeOffset start, DateTimeOffset? end) =>
        new(Guid.NewGuid(), Zone, start, end) { StaffMemberId = staffId };

    private static PortalResourceDraft Resource(ResourceCalculationType type, DateTimeOffset? start, DateTimeOffset? end) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Resource", type, Zone, start, end)
        {
            Quantity = type == ResourceCalculationType.Quantity ? 1 : null
        };
}
