using Operations.Domain.Enumerations;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.WorkOrders;

public sealed record WorkOrderServiceLineInput(
    ServiceSnapshot Service,
    IReadOnlyList<StaffMemberSnapshot> PerformedBy,
    TimeWindow Window,
    string? Description,
    bool IsReturnToRamp = false,
    Guid? Id = null);

public sealed record WorkOrderTaskInput(
    Guid? Id,
    TaskType TaskType,
    string? Description,
    TimeWindow Window,
    IReadOnlyList<StaffMemberSnapshot> Employees,
    IReadOnlyList<WorkOrderTaskToolInput> Tools,
    IReadOnlyList<WorkOrderTaskMaterialInput> Materials,
    IReadOnlyList<WorkOrderTaskGeneralSupportInput> GeneralSupports,
    bool IsReturnToRamp = false);

public sealed record WorkOrderTaskToolInput(ToolSnapshot Tool, Quantity Quantity);

public sealed record WorkOrderTaskMaterialInput(MaterialSnapshot Material, Quantity Quantity);

public sealed record WorkOrderTaskGeneralSupportInput(GeneralSupportSnapshot GeneralSupport, Quantity Quantity);
