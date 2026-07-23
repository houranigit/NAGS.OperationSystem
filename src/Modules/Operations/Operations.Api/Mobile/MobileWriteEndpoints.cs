using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Operations.Api.Endpoints;
using Operations.Application.Features.Mobile;
using Operations.Domain.Authorization;

namespace Operations.Api.Mobile;

/// <summary>
/// Write surface of the dedicated mobile BFF. Every mutation carries a client-generated
/// <c>clientMutationId</c> (the mobile outbox row id) so retries after lost responses replay
/// idempotently instead of duplicating work orders. Updates carry the cached base RowVersion so
/// an offline edit conflicts instead of overwriting work accepted by the portal in the meantime.
/// </summary>
internal static class MobileWriteEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        group.MapPost("/flights/{flightId:guid}/work-orders",
            async (Guid flightId, MobileWorkOrderWriteRequest request, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new MobileSubmitWorkOrderCommand(
                    flightId, request.WorkOrder.Type, request.WorkOrder.ToPayload(), request.ClientMutationId), ct);
                return ToWriteResult(result, created: true);
            }).RequirePermission(OperationsPermissions.WorkOrders.Author);

        group.MapPost("/work-orders/scratch",
            async (MobileScratchWorkOrderRequest request, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new MobileCreateScratchWorkOrderCommand(
                    request.CustomerId,
                    request.FlightNumber,
                    request.ScheduledArrivalUtc,
                    request.ScheduledDepartureUtc,
                    request.AircraftTypeId,
                    request.PlannedServiceIds ?? [],
                    request.WorkOrder.Type,
                    request.WorkOrder.ToPayload(),
                    request.ClientMutationId,
                    request.ClientFlightId), ct);
                return ToWriteResult(result, created: true);
            }).RequirePermission(OperationsPermissions.WorkOrders.Author);

        group.MapPut("/work-orders/{workOrderId:guid}",
            async (Guid workOrderId, MobileWorkOrderWriteRequest request, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new MobileUpdateWorkOrderCommand(
                    workOrderId,
                    request.WorkOrder.Type,
                    request.WorkOrder.ToPayload(),
                    request.ClientMutationId,
                    request.BaseRowVersion ?? string.Empty,
                    request.ServiceLineIdentityVersion), ct);
                return ToWriteResult(result, created: false);
            }).RequirePermission(OperationsPermissions.WorkOrders.Author);

        group.MapPost("/work-orders/{workOrderId:guid}/return-to-ramp",
            async (Guid workOrderId, MobileReturnToRampRequest request, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new MobileReturnToRampCommand(
                    workOrderId,
                    request.ServiceLines?.Select(l => new Operations.Application.Features.WorkOrders.WorkOrderServiceLineCommand(
                        l.ServiceId,
                        l.ResolvePerformedByStaffMemberIds(),
                        l.FromUtc,
                        l.ToUtc,
                        l.Description,
                        IsReturnToRamp: true,
                        Attachments: l.Attachments?.Select(a =>
                            new Operations.Application.Features.WorkOrders.WorkOrderServiceLineAttachmentCommand(
                                a.Kind,
                                a.Base64Content,
                                a.FileName,
                                a.ContentType)).ToList() ?? [])).ToList() ?? [],
                    request.Tasks?.Select(t => new Operations.Application.Features.WorkOrders.WorkOrderTaskCommand(
                        null,
                        t.TaskType,
                        t.Description,
                        t.FromUtc,
                        t.ToUtc,
                        t.EmployeeIds ?? [],
                        t.Tools?.Select(tool => new Operations.Application.Features.WorkOrders.WorkOrderTaskToolCommand(tool.ToolId, tool.Quantity)).ToList() ?? [],
                        t.Materials?.Select(m => new Operations.Application.Features.WorkOrders.WorkOrderTaskMaterialCommand(m.MaterialId, m.Quantity)).ToList() ?? [],
                        t.GeneralSupports?.Select(g => new Operations.Application.Features.WorkOrders.WorkOrderTaskGeneralSupportCommand(g.GeneralSupportId, g.Quantity)).ToList() ?? [],
                        t.Attachments?.Select(a => new Operations.Application.Features.WorkOrders.WorkOrderTaskAttachmentCommand(
                            a.Kind, a.Base64Content, a.FileName, a.ContentType)).ToList() ?? [],
                        IsReturnToRamp: true)).ToList() ?? [],
                    request.ClientMutationId), ct);
                return ToWriteResult(result, created: false);
            }).RequirePermission(OperationsPermissions.WorkOrders.Author);

        group.MapPost("/flights/{flightId:guid}/cancel",
            async (Guid flightId, MobileCancelFlightRequest request, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new MobileCancelFlightCommand(
                    flightId, request.CanceledAtUtc, request.Reason, request.ClientMutationId), ct);
                return ToWriteResult(result, created: true);
            }).RequirePermission(OperationsPermissions.WorkOrders.Author);

        group.MapPost("/flights/{flightId:guid}/invite",
            async (Guid flightId, MobileInviteRequest request, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new MobileInviteEmployeesCommand(
                    flightId, request.InviteeStaffMemberIds ?? []), ct);
                return result.ToNoContent();
            }).RequirePermission(OperationsPermissions.Flights.Invite);
    }

    private static IResult ToWriteResult(
        BuildingBlocks.Domain.Results.Result<MobileWriteResultDto> result,
        bool created)
    {
        if (result.IsFailure)
            return ApiResults.Problem(result.Error);

        // Replays answer 200 even on create routes so the client can tell a fresh write from an echo.
        return created && !result.Value.Idempotent
            ? Results.Created($"/api/v1/mobile/work-orders/{result.Value.WorkOrderId}", result.Value)
            : Results.Ok(result.Value);
    }
}

public sealed record MobileWorkOrderWriteRequest(
    string ClientMutationId,
    WorkOrderRequest WorkOrder,
    string? BaseRowVersion = null,
    int ServiceLineIdentityVersion = 0);

public sealed record MobileScratchWorkOrderRequest(
    string ClientMutationId,
    Guid ClientFlightId,
    Guid? CustomerId,
    string FlightNumber,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    Guid? AircraftTypeId,
    IReadOnlyList<Guid>? PlannedServiceIds,
    WorkOrderRequest WorkOrder);

public sealed record MobileReturnToRampRequest(
    string ClientMutationId,
    IReadOnlyList<WorkOrderServiceLineRequest>? ServiceLines,
    IReadOnlyList<WorkOrderTaskRequest>? Tasks);

public sealed record MobileCancelFlightRequest(
    string ClientMutationId,
    DateTimeOffset CanceledAtUtc,
    string Reason);

public sealed record MobileInviteRequest(IReadOnlyList<Guid>? InviteeStaffMemberIds);
