using MasterData.Contracts.Resources;

namespace Operations.Application.Contracts;

public sealed record WorkOrderPrintDto(
    WorkOrderDetailDto WorkOrder,
    WorkOrderPrintFlightDto Flight,
    string StationTimeZoneId,
    string? AircraftManufacturer,
    IReadOnlyList<WorkOrderPrintStaffDto> Staff,
    byte[]? CustomerSignatureContent,
    IReadOnlyDictionary<Guid, byte[]> ReturnToRampSignatures);

public sealed record WorkOrderDetailDto(
    Guid Id,
    string Status,
    string? OwnerName,
    string? CustomerIataCode,
    string CustomerName,
    string StationIata,
    string StationName,
    string OperationTypeName,
    string ActualFlightNumber,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    string? AircraftTypeModel,
    string? AircraftTailNumber,
    DateTimeOffset? ActualArrivalUtc,
    DateTimeOffset? ActualDepartureUtc,
    DateTimeOffset? CustomerSignedAtUtc,
    string? ApprovalNumber,
    DateTimeOffset? ApprovedAtUtc,
    IReadOnlyList<WorkOrderServiceLineDto> ServiceLines,
    IReadOnlyList<WorkOrderTaskDto> Tasks,
    DateTimeOffset? SubmittedAtUtc,
    IReadOnlyList<WorkOrderReturnToRampDto> ReturnToRamps,
    IReadOnlyList<WorkOrderPrintSubmissionDto> Submissions,
    string? WorkPackageNumber);

public sealed record WorkOrderPrintSubmissionDto(
    string Type, DateTimeOffset ActualAtUtc, string? Staff, string? RecordedBy,
    DateTimeOffset SubmittedAtUtc, string? Details);

public sealed record WorkOrderReturnToRampDto(
    Guid Id, int Sequence, DateTimeOffset FromUtc, DateTimeOffset? ToUtc, string? Description,
    IReadOnlyList<WorkOrderServiceLineDto> ServiceLines, IReadOnlyList<WorkOrderTaskDto> Tasks,
    DateTimeOffset? CustomerSignedAtUtc);

public sealed record WorkOrderPrintFlightDto(
    bool IsPerLanding, string? ScheduledAircraftManufacturer, string? ScheduledAircraftModel,
    IReadOnlyList<PlannedServiceDto> PlannedServices, IReadOnlyList<AssignedEmployeeDto> AssignedEmployees);

public sealed record WorkOrderPrintStaffDto(Guid StaffMemberId, string? ManpowerTypeName);

public sealed record WorkOrderServiceLineDto(
    string ServiceName, DateTimeOffset FromUtc, DateTimeOffset? ToUtc, string? Description,
    string? CustomerReferenceNumber, IReadOnlyList<WorkOrderPrintPerformerDto> PerformedBy,
    IReadOnlyList<WorkOrderPrintAttachmentDto> Attachments);

public sealed record WorkOrderTaskDto(
    string TaskType, string? Description, DateTimeOffset FromUtc, DateTimeOffset? ToUtc,
    string? AtaChapterCode, string? AtaChapterTitle, string? CustomerReferenceNumber,
    IReadOnlyList<WorkOrderPrintPerformerDto> Employees,
    IReadOnlyList<WorkOrderPrintResourceDto> Tools, IReadOnlyList<WorkOrderPrintResourceDto> Materials,
    IReadOnlyList<WorkOrderPrintResourceDto> GeneralSupports, IReadOnlyList<WorkOrderPrintAttachmentDto> Attachments);

public sealed record WorkOrderPrintPerformerDto(
    Guid StaffMemberId, string FullName, string EmployeeId, DateTimeOffset FromUtc, DateTimeOffset? ToUtc);

public sealed record WorkOrderPrintAttachmentDto(string Kind, string OriginalFileName, string ContentType, long Size);

public sealed record WorkOrderPrintResourceDto(
    string Name, ResourceCalculationType CalculationType, decimal? Quantity, DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc, string? Description);
