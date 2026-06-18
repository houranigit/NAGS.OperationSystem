using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.Employee;

namespace Core.Domain.Events;

public sealed class EmployeeActivatedEvent(EmployeeId employeeId) : DomainEvent
{
    public EmployeeId EmployeeId { get; } = employeeId;
}
