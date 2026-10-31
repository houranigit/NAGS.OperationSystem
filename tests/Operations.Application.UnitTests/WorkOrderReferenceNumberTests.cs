using Microsoft.EntityFrameworkCore;
using Operations.Application.Features.WorkOrders;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class WorkOrderReferenceNumberTests
{
    [Fact]
    public async Task References_survive_persistence_detail_reads_and_return_to_ramp_merge_cloning()
    {
        await using var db = new OperationsDbContext(new DbContextOptionsBuilder<OperationsDbContext>()
            .UseInMemoryDatabase($"work-order-references-{Guid.NewGuid()}").Options);
        var now = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        var window = TimeWindow.Create(now, now.AddHours(1)).Value;
        var staff = new StaffMemberSnapshot(Guid.NewGuid(), "Ramp Agent", "E1001");
        var service = new WorkOrderServiceLineInput(new ServiceSnapshot(Guid.NewGuid(), "Marshalling"),
            [new StaffMemberSnapshot(staff.StaffMemberId, staff.FullName, staff.EmployeeId)], TimeWindow.Create(window.From, window.To).Value, null, CustomerReferenceNumber: " SERVICE-REF ");
        var task = new WorkOrderTaskInput(null, TaskType.Minor, null, TimeWindow.Create(window.From, window.To).Value, [new StaffMemberSnapshot(staff.StaffMemberId, staff.FullName, staff.EmployeeId)], [], [], [],
            CustomerReferenceNumber: " TASK-REF ");
        var flight = Flight.ScheduleNew(new CustomerSnapshot(Guid.NewGuid(), "SV", "Saudia"),
            new StationSnapshot(Guid.NewGuid(), "RUH", "Riyadh"), new OperationTypeSnapshot(Guid.NewGuid(), "Transit"),
            FlightNumber.Create("SV1020").Value, ScheduledTime.Create(now, now.AddHours(1)).Value,
            null, [new ServiceSnapshot(service.Service.ServiceId, service.Service.Name)], [], null, null, Guid.NewGuid(), now).Value;
        var workOrder = WorkOrder.SubmitNew(flight, WorkOrderType.Completion, Guid.NewGuid(), new StaffMemberSnapshot(staff.StaffMemberId, staff.FullName, staff.EmployeeId),
            null, null, null, null, null, null, [service], [task],
            [new(null, window, "Return", [service with
                {
                    Service = new ServiceSnapshot(service.Service.ServiceId, service.Service.Name), PerformedBy = [new StaffMemberSnapshot(staff.StaffMemberId, staff.FullName, staff.EmployeeId)],
                    Window = TimeWindow.Create(window.From, window.To).Value, CustomerReferenceNumber = " RTR-SERVICE "
                }],
                [task with
                {
                    Employees = [new StaffMemberSnapshot(staff.StaffMemberId, staff.FullName, staff.EmployeeId)], Window = TimeWindow.Create(window.From, window.To).Value,
                    CustomerReferenceNumber = " RTR-TASK "
                }])], now,
            workPackageNumber: " PACKAGE-REF ").Value;
        db.Flights.Add(flight);
        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var stored = await WorkOrderLoader.ForMutation(db.WorkOrders).SingleAsync();
        var detail = WorkOrderDtoMapper.Detail(stored);
        detail.WorkPackageNumber.ShouldBe("PACKAGE-REF");
        detail.ServiceLines.Single().CustomerReferenceNumber.ShouldBe("SERVICE-REF");
        detail.Tasks.Single().CustomerReferenceNumber.ShouldBe("TASK-REF");
        var returned = detail.ReturnToRamps!.Single();
        returned.ServiceLines.Single().CustomerReferenceNumber.ShouldBe("RTR-SERVICE");
        returned.Tasks.Single().CustomerReferenceNumber.ShouldBe("RTR-TASK");

        var cloned = WorkOrderReturnToRampCloner.Clone([stored]).Single();
        cloned.ServiceLines.Single().CustomerReferenceNumber.ShouldBe("RTR-SERVICE");
        cloned.Tasks.Single().CustomerReferenceNumber.ShouldBe("RTR-TASK");

        foreach (var (type, name) in new[]
                 {
                     (typeof(WorkOrder), nameof(WorkOrder.WorkPackageNumber)),
                     (typeof(WorkOrderServiceLine), nameof(WorkOrderServiceLine.CustomerReferenceNumber)),
                     (typeof(WorkOrderTask), nameof(WorkOrderTask.CustomerReferenceNumber))
                 })
        {
            var property = db.Model.FindEntityType(type)!.FindProperty(name)!;
            property.IsNullable.ShouldBeTrue();
            property.GetMaxLength().ShouldBe(200);
        }
    }
}
