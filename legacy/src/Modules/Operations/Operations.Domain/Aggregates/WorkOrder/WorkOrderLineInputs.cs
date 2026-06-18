using Core.Contracts.Features.Employee;
using Core.Contracts.Features.Service;
using Operations.Domain.Entities;
using Operations.Domain.Enumerations;
using Store.Contracts.Features.GeneralSupport;
using Store.Contracts.Features.Material;
using Store.Contracts.Features.Tool;

namespace Operations.Domain.Aggregates.WorkOrder;

/// <summary>One service line on a work order — unchanged from the legacy model.</summary>
public readonly record struct WorkOrderServiceLineInput(
    ServiceSnapshot Service,
    EmployeeSnapshot Employee,
    DateTimeOffset From,
    DateTimeOffset To,
    string? Description,
    bool ReturnToRamp);

/// <summary>
/// One unified task on a work order — replaces the legacy
/// <c>WorkOrderEmployeeLineInput</c> and <c>WorkOrderCorrectiveActionInput</c>. A task is
/// optional store usage (tools / materials / general supports), participating employees,
/// a description, a time window, attachments, and an RTR flag — see
/// <see cref="WorkOrderTask"/>.
/// </summary>
public readonly record struct WorkOrderTaskInput(
    TaskType TaskType,
    string? Description,
    DateTimeOffset From,
    DateTimeOffset To,
    bool ReturnToRamp,
    IReadOnlyList<EmployeeSnapshot> Employees,
    IReadOnlyList<ToolSnapshot> Tools,
    IReadOnlyList<MaterialSnapshot> Materials,
    IReadOnlyList<GeneralSupportSnapshot> GeneralSupports,
    IReadOnlyList<TaskAttachmentInput> Attachments);
