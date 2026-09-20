using Operations.Domain.Enumerations;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.WorkOrders;

public sealed record WorkOrderServiceLineInput(
    ServiceSnapshot Service,
    IReadOnlyList<StaffMemberSnapshot> PerformedBy,
    TimeWindow Window,
    string? Description,
    bool IsReturnToRamp = false,
    Guid? Id = null,
    IReadOnlyList<WorkOrderEmployeeAssignmentInput>? EmployeeAssignments = null);

public sealed record WorkOrderTaskInput(
    Guid? Id,
    TaskType TaskType,
    string? Description,
    TimeWindow Window,
    IReadOnlyList<StaffMemberSnapshot> Employees,
    IReadOnlyList<WorkOrderTaskToolInput> Tools,
    IReadOnlyList<WorkOrderTaskMaterialInput> Materials,
    IReadOnlyList<WorkOrderTaskGeneralSupportInput> GeneralSupports,
    bool IsReturnToRamp = false,
    IReadOnlyList<WorkOrderEmployeeAssignmentInput>? EmployeeAssignments = null,
    AtaChapterSnapshot? AtaChapter = null);

public sealed record WorkOrderEmployeeAssignmentInput(Guid StaffMemberId, TimeWindow Window);

/// <summary>
/// One distinct return-to-ramp occurrence. Service lines and tasks belong to this occurrence and
/// are no longer classified by independent Boolean flags.
/// </summary>
public sealed record WorkOrderReturnToRampInput(
    Guid? Id,
    TimeWindow Window,
    string? Description,
    IReadOnlyList<WorkOrderServiceLineInput> ServiceLines,
    IReadOnlyList<WorkOrderTaskInput> Tasks);

public sealed record WorkOrderTaskToolInput(ToolSnapshot Tool, ResourceUsage Usage, string? Description = null);

public sealed record WorkOrderTaskMaterialInput(MaterialSnapshot Material, ResourceUsage Usage, string? Description = null);

public sealed record WorkOrderTaskGeneralSupportInput(GeneralSupportSnapshot GeneralSupport, ResourceUsage Usage, string? Description = null);
