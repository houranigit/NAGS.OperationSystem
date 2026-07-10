using Operations.Domain.Enumerations;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.WorkOrders;

public sealed record WorkOrderServiceLineInput(
    ServiceSnapshot Service,
    StaffMemberSnapshot PerformedBy,
    TimeWindow Window,
    string? Description);

public sealed record WorkOrderTaskInput(
    Guid? Id,
    TaskType TaskType,
    string? Description,
    TimeWindow Window,
    IReadOnlyList<StaffMemberSnapshot> Employees,
    IReadOnlyList<WorkOrderTaskToolInput> Tools,
    IReadOnlyList<WorkOrderTaskMaterialInput> Materials,
    IReadOnlyList<WorkOrderTaskGeneralSupportInput> GeneralSupports);

public sealed record WorkOrderTaskToolInput(ToolSnapshot Tool, Quantity Quantity);

public sealed record WorkOrderTaskMaterialInput(MaterialSnapshot Material, Quantity Quantity);

public sealed record WorkOrderTaskGeneralSupportInput(GeneralSupportSnapshot GeneralSupport, Quantity Quantity);
