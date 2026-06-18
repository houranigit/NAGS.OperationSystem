using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.Employee;

namespace Core.Domain.Events;

public sealed class EmployeeLinkedToUserEvent(EmployeeId employeeId, Guid linkedUserId) : DomainEvent
{
    public EmployeeId EmployeeId { get; } = employeeId;
    public Guid LinkedUserId { get; } = linkedUserId;
}
