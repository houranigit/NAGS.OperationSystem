using System.Text.Json.Serialization;

namespace OperationsSystem.Blazor.Client.Api;

public sealed record PortalFlightOperations(
    Guid Id, string FlightNumber, Guid CustomerId, string? CustomerIataCode, string CustomerName,
    Guid StationId, string StationIata, string OperationTypeName, Guid? AircraftTypeId, string? AircraftTypeModel,
    DateTimeOffset ScheduledArrivalUtc, DateTimeOffset ScheduledDepartureUtc, string RowVersion,
    string StationTimeZoneId, Guid? CurrentStaffMemberId, bool IsAdHocFlight, bool IsUnassignedSearchFlight,
    IReadOnlyList<PlannedServiceModel> PlannedServices,
    IReadOnlyList<PortalFlightArrival> ArrivalSubmissions, bool CanSaveArrival,
    IReadOnlyList<PortalFlightDeparture> DepartureSubmissions, bool CanSaveDeparture,
    IReadOnlyList<PortalFlightCancellation> CancellationSubmissions, bool CanSaveCancellation,
    IReadOnlyList<PortalFlightService> Services, bool CanSaveServices, IReadOnlyList<Guid>? AllowedServiceIds,
    IReadOnlyList<PortalFlightTask> Tasks, bool CanSaveTasks, bool? CanAddServices, bool? CanAddTasks,
    IReadOnlyList<PortalFlightReturnToRamp> ReturnToRamps, bool CanSaveReturnsToRamp, int LastReturnToRampSequence);

public sealed record PortalStaffSnapshot(Guid StaffMemberId, string FullName, string EmployeeId);

public sealed record PortalFlightArrival(Guid Id, PortalStaffSnapshot SubmittedByEmployee, DateTimeOffset Ata,
    Guid AircraftTypeId, string AircraftTypeModel, string AircraftTailNumber, string? CustomerWorkReferenceNumber,
    PortalStaffSnapshot ReceivedByEmployee, string? Remarks, bool IsAccepted, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc = null, string? CreatedByDisplayName = null, string? UpdatedByDisplayName = null);

public sealed record PortalFlightDeparture(Guid Id, PortalStaffSnapshot SubmittedByEmployee, DateTimeOffset Atd,
    PortalStaffSnapshot HandledByEmployee, string? Remarks, bool IsAccepted, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc, bool HasSignature, string? CreatedByDisplayName = null, string? UpdatedByDisplayName = null);

public sealed record PortalFlightCancellation(Guid Id, PortalStaffSnapshot SubmittedByEmployee, DateTimeOffset CanceledAtUtc,
    string? Remarks, bool IsAccepted, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc);

public sealed record PortalFlightPerformer(Guid Id, Guid StaffMemberId, string FullName, string EmployeeId,
    DateTimeOffset StartUtc, DateTimeOffset? EndUtc, string? Notes = null);

public sealed record PortalFlightAttachment(Guid Id, int Kind, string FileName, string ContentType,
    long SizeBytes, DateTimeOffset UploadedAtUtc);

public sealed record PortalFlightResource(Guid Id, Guid ResourceId, string Name, ResourceCalculationType CalculationType,
    decimal? Quantity, DateTimeOffset? StartUtc, DateTimeOffset? EndUtc, string? Notes);

public sealed record PortalFlightService(Guid Id, Guid ServiceId, string ServiceName, DateTimeOffset StartUtc,
    DateTimeOffset? EndUtc, string? CustomerReferenceNumber, string? Notes, IReadOnlyList<PortalFlightPerformer> Performers,
    IReadOnlyList<PortalFlightAttachment> Attachments, bool CanEdit, bool CanSetEnd, bool CanDelete,
    bool CanChangeService, string? CreatedByDisplayName, string? UpdatedByDisplayName, Guid CreatedByUserId,
    bool CanCloseOnly, DateTimeOffset? CreatedAtUtc = null, DateTimeOffset? UpdatedAtUtc = null);

public sealed record PortalFlightTask(Guid Id, int TaskType, Guid? AtaChapterId, string? AtaChapterCode,
    string? AtaChapterTitle, DateTimeOffset StartUtc, DateTimeOffset? EndUtc, string? CustomerReferenceNumber, string? Notes,
    IReadOnlyList<PortalFlightPerformer> Performers, IReadOnlyList<PortalFlightResource> Tools,
    IReadOnlyList<PortalFlightResource> Materials, IReadOnlyList<PortalFlightResource> GeneralSupports,
    IReadOnlyList<PortalFlightAttachment> Attachments, bool CanEdit, bool CanSetEnd, bool CanDelete, bool CanChangeType,
    string? CreatedByDisplayName, string? UpdatedByDisplayName, Guid CreatedByUserId, bool CanCloseOnly,
    DateTimeOffset? CreatedAtUtc = null, DateTimeOffset? UpdatedAtUtc = null);

public sealed record PortalFlightReturnToRamp(Guid Id, int Sequence, DateTimeOffset ArrivedAtUtc, string? ArrivalRemarks,
    string ReceivedByFullName, DateTimeOffset? DepartedAtUtc, string? DepartureRemarks, string? HandledByFullName,
    bool HasSignature, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, string? CreatedByDisplayName,
    string? UpdatedByDisplayName, bool CanEdit, bool IsOpen, IReadOnlyList<PortalFlightService> Services,
    IReadOnlyList<PortalFlightTask> Tasks, Guid CreatedByUserId);

public sealed record PortalWorkBatchRequest(
    PortalFlightArrivalRequest? Arrival, PortalFlightDepartureRequest? Departure,
    IReadOnlyList<PortalServiceChange> ServiceChanges, IReadOnlyList<PortalTaskChange> TaskChanges);

public sealed record PortalFlightArrivalRequest(DateTimeOffset Ata, Guid AircraftTypeId, string AircraftTailNumber,
    string? CustomerWorkReferenceNumber, string? Remarks, bool AcknowledgeConflictingSubmission, Guid? TargetSubmissionId);

public sealed record PortalFlightDepartureRequest(DateTimeOffset Atd, byte[]? SignatureContent,
    DateTimeOffset? SignatureSignedAtUtc, string? Remarks, bool AcknowledgeConflictingSubmission, Guid? TargetSubmissionId);

public sealed record PortalServiceChange(Guid RecordId, PortalFlightServiceRequest? Input);
public sealed record PortalTaskChange(Guid RecordId, PortalFlightTaskRequest? Input);

public sealed record PortalFlightServiceRequest(Guid ServiceId, DateTimeOffset StartUtc, DateTimeOffset? EndUtc,
    string? CustomerReferenceNumber, string? Notes, IReadOnlyList<PortalFlightPerformerRequest> Performers,
    IReadOnlyList<PortalFlightAttachmentRequest> Attachments);

public sealed record PortalFlightTaskRequest(int TaskType, Guid? AtaChapterId, DateTimeOffset StartUtc,
    DateTimeOffset? EndUtc, string? CustomerReferenceNumber, string? Notes, IReadOnlyList<PortalFlightPerformerRequest> Performers,
    IReadOnlyList<PortalFlightResourceRequest> Tools, IReadOnlyList<PortalFlightResourceRequest> Materials,
    IReadOnlyList<PortalFlightResourceRequest> GeneralSupports, IReadOnlyList<PortalFlightAttachmentRequest> Attachments);

public sealed record PortalFlightPerformerRequest(Guid Id, Guid StaffMemberId, DateTimeOffset StartUtc,
    DateTimeOffset? EndUtc, string? Notes = null);

public sealed record PortalFlightResourceRequest(Guid Id, Guid ResourceId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal? Quantity,
    DateTimeOffset? StartUtc,
    DateTimeOffset? EndUtc, string? Notes);

public sealed record PortalFlightAttachmentRequest(Guid Id, int Kind, string FileName, string ContentType, byte[]? Content);

public sealed record PortalReturnToRampRequest(PortalReturnToRampArrivalRequest Arrival,
    PortalReturnToRampDepartureRequest? Departure, IReadOnlyList<PortalServiceChange> Services,
    IReadOnlyList<PortalTaskChange> Tasks);

public sealed record PortalReturnToRampArrivalRequest(DateTimeOffset ArrivedAtUtc, string? Remarks);
public sealed record PortalReturnToRampDepartureRequest(DateTimeOffset DepartedAtUtc, string? Remarks,
    byte[]? SignatureContent, DateTimeOffset? SignatureSignedAtUtc, bool RemoveSignature);
