using Operations.Domain.Flights;

namespace Operations.Application.Contracts;

public sealed record SaveFlightServicePerformerRequest(Guid Id, Guid StaffMemberId, DateTimeOffset StartUtc, DateTimeOffset? EndUtc = null);

/// <summary>Content is base64 bytes for a new file; a retained immutable attachment omits content.</summary>
public sealed record SaveFlightServiceAttachmentRequest(Guid Id, int Kind, string FileName, string ContentType, byte[]? Content = null);

public sealed record SaveFlightServiceRequest(Guid ServiceId, DateTimeOffset StartUtc, DateTimeOffset? EndUtc,
    string? CustomerReferenceNumber, string? Notes, IReadOnlyList<SaveFlightServicePerformerRequest> Performers,
    IReadOnlyList<SaveFlightServiceAttachmentRequest>? Attachments = null);

public sealed record FlightServicePerformerDto(Guid Id, Guid StaffMemberId, string FullName, string EmployeeId,
    DateTimeOffset StartUtc, DateTimeOffset? EndUtc);

public sealed record FlightServiceAttachmentDto(Guid Id, int Kind, string FileName, string ContentType,
    long SizeBytes, Guid UploadedByUserId, DateTimeOffset UploadedAtUtc)
{
    public static FlightServiceAttachmentDto Map(FlightServiceAttachment attachment) => new(attachment.Id, (int)attachment.Details.Kind,
        attachment.Details.FileName, attachment.Details.ContentType, attachment.Details.SizeBytes,
        attachment.UploadedByUserId, attachment.UploadedAtUtc);
}

public sealed record FlightServiceDto(Guid Id, Guid ServiceId, string ServiceName, DateTimeOffset StartUtc, DateTimeOffset? EndUtc,
    string? CustomerReferenceNumber, string? Notes, IReadOnlyList<FlightServicePerformerDto> Performers,
    Guid CreatedByUserId, Guid? EndedByUserId, DateTimeOffset CreatedAtUtc, Guid? UpdatedByUserId, DateTimeOffset? UpdatedAtUtc,
    bool CanEdit, bool CanSetEnd, IReadOnlyList<FlightServiceAttachmentDto> Attachments,
    bool CanChangeService = false, bool CanDelete = false, string? CreatedByDisplayName = null, string? EndedByDisplayName = null)
{
    public static FlightServiceDto Map(FlightService service, Guid? userId = null, bool canOperate = false,
        bool assignedCorrection = false, string? createdByDisplayName = null, string? endedByDisplayName = null) => new(
        service.Id, service.Service.ServiceId, service.Service.Name, service.Window.FromUtc, service.Window.ToUtc,
        service.CustomerReferenceNumber, service.Notes,
        service.Performers.OrderBy(p => p.Id).Select(p => new FlightServicePerformerDto(p.Id, p.Employee.StaffMemberId,
            p.Employee.FullName, p.Employee.EmployeeId, p.Window.FromUtc, p.Window.ToUtc)).ToList(),
        service.CreatedByUserId, service.EndedByUserId, service.CreatedAtUtc, service.UpdatedByUserId, service.UpdatedAtUtc,
        canOperate && (userId == service.CreatedByUserId || assignedCorrection),
        canOperate && (assignedCorrection || service.Window.ToUtc is null || userId == service.EndedByUserId),
        service.Attachments.OrderBy(a => a.UploadedAtUtc).ThenBy(a => a.Id).Select(FlightServiceAttachmentDto.Map).ToList(),
        canOperate && (assignedCorrection || userId == service.CreatedByUserId && !service.HasOtherUserEnd),
        canOperate && (assignedCorrection || userId == service.CreatedByUserId && (service.Window.ToUtc is null || userId == service.EndedByUserId)),
        createdByDisplayName, endedByDisplayName);
}

public sealed record FlightServiceSavedDto(FlightServiceDto Service, string RowVersion, int ActiveWorkCount, int CompletedWorkCount);
public sealed record FlightServiceRemovedDto(Guid Id, string RowVersion, int ActiveWorkCount, int CompletedWorkCount);
