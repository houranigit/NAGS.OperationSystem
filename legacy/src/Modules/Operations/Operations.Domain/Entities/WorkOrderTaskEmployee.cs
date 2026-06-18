using BuildingBlocks.Domain.Entities;
using Core.Contracts.Features.Employee;

namespace Operations.Domain.Entities;

/// <summary>One employee participating in a <see cref="WorkOrderTask"/>.</summary>
public sealed class WorkOrderTaskEmployee : Entity<Guid>
{
    public Guid TaskId { get; private set; }
    public EmployeeSnapshot Employee { get; private set; } = null!;

    private WorkOrderTaskEmployee()
    {
    }

    internal WorkOrderTaskEmployee(Guid id, Guid taskId, EmployeeSnapshot employee)
    {
        Id = id;
        TaskId = taskId;
        Employee = employee;
    }
}
