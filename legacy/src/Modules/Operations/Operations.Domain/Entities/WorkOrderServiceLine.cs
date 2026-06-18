using BuildingBlocks.Domain.Entities;
using Core.Contracts.Features.Employee;
using Core.Contracts.Features.Service;
using Operations.Domain.Aggregates.WorkOrder;

namespace Operations.Domain.Entities;

public sealed class WorkOrderServiceLine : Entity<Guid>
{
    public WorkOrderId WorkOrderId { get; private set; } = null!;
    public ServiceSnapshot Service { get; private set; } = null!;
    public EmployeeSnapshot Employee { get; private set; } = null!;
    public DateTimeOffset From { get; private set; }
    public DateTimeOffset To { get; private set; }
    public string? Description { get; private set; }
    public bool ReturnToRamp { get; private set; }

    private WorkOrderServiceLine()
    {
    }

    internal WorkOrderServiceLine(
        Guid id,
        WorkOrderId workOrderId,
        ServiceSnapshot service,
        EmployeeSnapshot employee,
        DateTimeOffset from,
        DateTimeOffset to,
        string? description,
        bool returnToRamp)
    {
        Id = id;
        WorkOrderId = workOrderId;
        Service = service;
        Employee = employee;
        From = from;
        To = to;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ReturnToRamp = returnToRamp;
    }
}
