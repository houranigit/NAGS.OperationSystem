using Core.Contracts.Features.Service;
using Core.Contracts.Features.Employee;

namespace Operations.Contracts.WorkOrder
{
    public sealed record WorkOrderServiceLineDto(
        Guid Id,
        ServiceSnapshot ServiceSnapshot,
        EmployeeSnapshot EmployeeSnapshot,
        WorkOrderSnapshot WorkOrderSnapshot,
        DateTimeOffset From,
        DateTimeOffset To,
        string? Description,
        bool ReturnToRamp);
}
