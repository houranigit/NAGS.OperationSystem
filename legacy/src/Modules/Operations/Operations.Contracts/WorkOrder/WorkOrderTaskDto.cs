using Core.Contracts.Features.Employee;
using Operations.Domain.Enumerations;
using Store.Contracts.Features.GeneralSupport;
using Store.Contracts.Features.Material;
using Store.Contracts.Features.Tool;

namespace Operations.Contracts.WorkOrder;

/// <summary>
/// Read-side projection of a <see cref="Operations.Domain.Entities.WorkOrderTask"/>. Replaces
/// the legacy <c>WorkOrderEmployeeLineDto</c> + <c>WorkOrderCorrectiveActionLineDto</c>
/// pair with a single richer shape that carries per-task severity, attachments, and store
/// usage.
/// </summary>
public sealed record WorkOrderTaskDto(
    Guid Id,
    TaskType TaskType,
    string? Description,
    DateTimeOffset From,
    DateTimeOffset To,
    bool ReturnToRamp,
    IReadOnlyList<EmployeeSnapshot> Employees,
    IReadOnlyList<ToolSnapshot> Tools,
    IReadOnlyList<MaterialSnapshot> Materials,
    IReadOnlyList<GeneralSupportSnapshot> GeneralSupports,
    IReadOnlyList<WorkOrderTaskAttachmentDto> Attachments);

/// <summary>
/// Inline binary attachment on a task — image, voice, or document. <see cref="Bytes"/> is
/// the full payload (System.Text.Json serialises it as a Base64 string for the portal /
/// mobile clients).
/// </summary>
public sealed record WorkOrderTaskAttachmentDto(
    Guid Id,
    TaskAttachmentKind Kind,
    string ContentType,
    string FileName,
    int SizeBytes,
    DateTimeOffset CapturedAt,
    byte[] Bytes);
