using System.Text.Json.Serialization;
using Operations.Domain.Flights;

namespace Operations.Application.Contracts;

public sealed record SaveFlightTaskPerformerRequest(Guid Id, Guid StaffMemberId, DateTimeOffset StartUtc,
    DateTimeOffset? EndUtc = null, string? Notes = null);

public sealed record SaveFlightTaskResourceRequest(Guid Id, Guid ResourceId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal? Quantity = null,
    DateTimeOffset? StartUtc = null, DateTimeOffset? EndUtc = null, string? Notes = null);

public sealed record SaveFlightTaskRequest(int TaskType, Guid? AtaChapterId, DateTimeOffset StartUtc, DateTimeOffset? EndUtc,
    string? CustomerReferenceNumber, string? Notes, IReadOnlyList<SaveFlightTaskPerformerRequest> Performers,
    IReadOnlyList<SaveFlightTaskResourceRequest> Tools, IReadOnlyList<SaveFlightTaskResourceRequest> Materials,
    IReadOnlyList<SaveFlightTaskResourceRequest> GeneralSupports, IReadOnlyList<SaveFlightServiceAttachmentRequest>? Attachments = null);

public sealed record FlightTaskPerformerDto(Guid Id, Guid StaffMemberId, string FullName, string EmployeeId,
    DateTimeOffset StartUtc, DateTimeOffset? EndUtc, string? Notes);

public sealed record FlightTaskResourceDto(Guid Id, Guid ResourceId, string Name, int CalculationType,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal? Quantity,
    DateTimeOffset? StartUtc, DateTimeOffset? EndUtc, string? Notes);

public sealed record FlightTaskDto(Guid Id, int TaskType, Guid? AtaChapterId, string? AtaChapterCode, string? AtaChapterTitle,
    DateTimeOffset StartUtc, DateTimeOffset? EndUtc, string? CustomerReferenceNumber, string? Notes,
    IReadOnlyList<FlightTaskPerformerDto> Performers, IReadOnlyList<FlightTaskResourceDto> Tools,
    IReadOnlyList<FlightTaskResourceDto> Materials, IReadOnlyList<FlightTaskResourceDto> GeneralSupports,
    IReadOnlyList<FlightServiceAttachmentDto> Attachments, Guid CreatedByUserId, Guid? EndedByUserId, DateTimeOffset CreatedAtUtc,
    Guid? UpdatedByUserId, DateTimeOffset? UpdatedAtUtc, bool CanEdit, bool CanSetEnd, bool CanDelete, bool CanChangeType, string? CreatedByDisplayName = null, string? EndedByDisplayName = null)
{
    public static FlightTaskDto Map(FlightTask task, Guid? userId = null, bool canOperate = false,
        bool assignedCorrection = false, string? createdByDisplayName = null, string? endedByDisplayName = null) => new(
        task.Id, (int)task.TaskType, task.AtaChapter?.AtaChapterId, task.AtaChapter?.Code, task.AtaChapter?.Title,
        task.Window.FromUtc, task.Window.ToUtc, task.CustomerReferenceNumber, task.Notes,
        task.Performers.OrderBy(item => item.Id).Select(item => new FlightTaskPerformerDto(item.Id, item.Employee.StaffMemberId,
            item.Employee.FullName, item.Employee.EmployeeId, item.Window.FromUtc, item.Window.ToUtc, item.Notes)).ToList(),
        task.Tools.OrderBy(item => item.Id).Select(item => new FlightTaskResourceDto(item.Id, item.Tool.ToolId, item.Tool.Name,
            (int)item.Tool.CalculationType, item.Usage.Quantity, item.Usage.FromUtc, item.Usage.ToUtc, item.Notes)).ToList(),
        task.Materials.OrderBy(item => item.Id).Select(item => new FlightTaskResourceDto(item.Id, item.Material.MaterialId, item.Material.Name,
            (int)item.Material.CalculationType, item.Usage.Quantity, item.Usage.FromUtc, item.Usage.ToUtc, item.Notes)).ToList(),
        task.GeneralSupports.OrderBy(item => item.Id).Select(item => new FlightTaskResourceDto(item.Id, item.GeneralSupport.GeneralSupportId, item.GeneralSupport.Name,
            (int)item.GeneralSupport.CalculationType, item.Usage.Quantity, item.Usage.FromUtc, item.Usage.ToUtc, item.Notes)).ToList(),
        task.Attachments.OrderBy(item => item.UploadedAtUtc).ThenBy(item => item.Id).Select(item => new FlightServiceAttachmentDto(item.Id,
            (int)item.Details.Kind, item.Details.FileName, item.Details.ContentType, item.Details.SizeBytes, item.UploadedByUserId, item.UploadedAtUtc)).ToList(),
        task.CreatedByUserId, task.EndedByUserId, task.CreatedAtUtc, task.UpdatedByUserId, task.UpdatedAtUtc,
        canOperate && (userId == task.CreatedByUserId || assignedCorrection),
        canOperate && (assignedCorrection || task.Window.ToUtc is null || userId == task.EndedByUserId),
        canOperate && (assignedCorrection || userId == task.CreatedByUserId && (task.Window.ToUtc is null || userId == task.EndedByUserId)),
        canOperate && (assignedCorrection || userId == task.CreatedByUserId),
        createdByDisplayName, endedByDisplayName);
}

public sealed record FlightTaskSavedDto(FlightTaskDto Task, string RowVersion, int ActiveWorkCount, int CompletedWorkCount);
public sealed record FlightTaskRemovedDto(Guid Id, string RowVersion, int ActiveWorkCount, int CompletedWorkCount);
