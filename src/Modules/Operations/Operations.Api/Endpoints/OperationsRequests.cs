using Operations.Application.Features.WorkOrders;
using Operations.Domain.Enumerations;

namespace Operations.Api.Endpoints;

public sealed record ScheduleFlightRequest(
    Guid CustomerId,
    Guid StationId,
    Guid OperationTypeId,
    string FlightNumber,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    Guid? AircraftTypeId,
    IReadOnlyList<Guid>? PlannedServiceIds,
    IReadOnlyList<Guid>? AssignedStaffMemberIds);

public sealed record ScheduleFlightsRequest(
    Guid CustomerId,
    Guid StationId,
    Guid OperationTypeId,
    string FlightNumber,
    TimeOnly ScheduledArrivalTimeUtc,
    TimeOnly ScheduledDepartureTimeUtc,
    IReadOnlyList<DateOnly>? SelectedDates,
    Guid? AircraftTypeId,
    IReadOnlyList<Guid>? PlannedServiceIds,
    IReadOnlyList<Guid>? AssignedStaffMemberIds);

public sealed record UpdateScheduledFlightRequest(
    Guid CustomerId,
    Guid StationId,
    Guid OperationTypeId,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    Guid? AircraftTypeId,
    IReadOnlyList<Guid>? PlannedServiceIds);

public sealed record ChangeFlightNumberRequest(string FlightNumber);

public sealed record AssignEmployeesRequest(IReadOnlyList<Guid>? StaffMemberIds);

public sealed record MergeFlightsRequest(Guid SurvivorFlightId, Guid LoserFlightId);

public sealed record PerLandingApprovalSelectionRequest(Guid FlightId, Guid WorkOrderId, byte[] RowVersion);

public sealed record ApprovePerLandingFlightsRequest(IReadOnlyList<PerLandingApprovalSelectionRequest>? Selections)
{
    public ApprovePerLandingFlightsCommand ToCommand() => new(
        Selections?.Select(x => new PerLandingApprovalSelection(x.FlightId, x.WorkOrderId, x.RowVersion)).ToList() ?? []);
}

public sealed record CreateAdHocWorkOrderRequest(
    Guid CustomerId,
    Guid StationId,
    string FlightNumber,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    Guid? AircraftTypeId,
    IReadOnlyList<Guid>? PlannedServiceIds,
    IReadOnlyList<Guid>? AssignedStaffMemberIds,
    WorkOrderRequest WorkOrder)
{
    public CreateAdHocWorkOrderCommand ToCommand() =>
        new(
            CustomerId,
            StationId,
            FlightNumber,
            ScheduledArrivalUtc,
            ScheduledDepartureUtc,
            AircraftTypeId,
            PlannedServiceIds ?? [],
            AssignedStaffMemberIds ?? [],
            WorkOrder.Type,
            WorkOrder.ToPayload());
}

public sealed record WorkOrderRequest(
    WorkOrderType Type,
    string? ActualFlightNumber,
    Guid? AircraftTypeId,
    string? AircraftTailNumber,
    DateTimeOffset? ActualArrivalUtc,
    DateTimeOffset? ActualDepartureUtc,
    DateTimeOffset? CanceledAtUtc,
    string? CancellationReason,
    string? Remarks,
    IReadOnlyList<WorkOrderServiceLineRequest>? ServiceLines,
    IReadOnlyList<WorkOrderTaskRequest>? Tasks,
    WorkOrderSignatureRequest? CustomerSignature = null)
{
    public WorkOrderEditableCommandPayload ToPayload() =>
        new(
            ActualFlightNumber,
            AircraftTypeId,
            AircraftTailNumber,
            ActualArrivalUtc,
            ActualDepartureUtc,
            CanceledAtUtc,
            CancellationReason,
            Remarks,
            ServiceLines?.Select(l => new WorkOrderServiceLineCommand(
                l.ServiceId,
                l.PerformedByStaffMemberId,
                l.FromUtc,
                l.ToUtc,
                l.Description)).ToList() ?? [],
            Tasks?.Select(t => new WorkOrderTaskCommand(
                t.Id,
                t.TaskType,
                t.Description,
                t.FromUtc,
                t.ToUtc,
                t.EmployeeIds ?? [],
                t.Tools?.Select(tool => new WorkOrderTaskToolCommand(tool.ToolId, tool.Quantity)).ToList() ?? [],
                t.Materials?.Select(material => new WorkOrderTaskMaterialCommand(material.MaterialId, material.Quantity)).ToList() ?? [],
                t.GeneralSupports?.Select(support => new WorkOrderTaskGeneralSupportCommand(support.GeneralSupportId, support.Quantity)).ToList() ?? [],
                t.Attachments?.Select(attachment => new WorkOrderTaskAttachmentCommand(
                    attachment.Kind,
                    attachment.Base64Content,
                    attachment.FileName,
                    attachment.ContentType)).ToList() ?? [])).ToList() ?? [],
            CustomerSignature is null
                ? null
                : new WorkOrderSignatureCommand(
                    CustomerSignature.Base64Content,
                    CustomerSignature.FileName,
                    CustomerSignature.ContentType));
}

public sealed record MergeWorkOrdersRequest(
    IReadOnlyList<Guid>? SourceWorkOrderIds,
    WorkOrderRequest WorkOrder,
    bool ApproveImmediately)
{
    public MergeWorkOrdersCommand ToCommand(Guid flightId) =>
        new(
            flightId,
            SourceWorkOrderIds ?? [],
            WorkOrder.Type,
            WorkOrder.ToPayload(),
            ApproveImmediately);
}

public sealed record WorkOrderServiceLineRequest(
    Guid ServiceId,
    Guid PerformedByStaffMemberId,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? Description);

public sealed record WorkOrderTaskRequest(
    Guid? Id,
    TaskType TaskType,
    string? Description,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    IReadOnlyList<Guid>? EmployeeIds,
    IReadOnlyList<WorkOrderTaskToolRequest>? Tools,
    IReadOnlyList<WorkOrderTaskMaterialRequest>? Materials,
    IReadOnlyList<WorkOrderTaskGeneralSupportRequest>? GeneralSupports,
    IReadOnlyList<WorkOrderTaskAttachmentRequest>? Attachments = null);

public sealed record WorkOrderTaskToolRequest(Guid ToolId, decimal Quantity);

public sealed record WorkOrderTaskMaterialRequest(Guid MaterialId, decimal Quantity);

public sealed record WorkOrderTaskGeneralSupportRequest(Guid GeneralSupportId, decimal Quantity);

public sealed record WorkOrderTaskAttachmentRequest(
    TaskAttachmentKind Kind,
    string Base64Content,
    string FileName,
    string ContentType);

public sealed record WorkOrderSignatureRequest(
    string Base64Content,
    string FileName,
    string ContentType);

public sealed record ReturnWorkOrderRequest(string Reason);
