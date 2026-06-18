using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.Employee;

namespace Core.Domain.Events;

public sealed class EmployeeCreatedEvent(
    EmployeeId employeeId,
    string fullName,
    string email,
    bool createUser) : DomainEvent
{
    public EmployeeId EmployeeId { get; } = employeeId;
    public string FullName { get; } = fullName;
    public string Email { get; } = email;
    public bool CreateUser { get; } = createUser;
}
