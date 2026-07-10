using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Concurrency;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Application.Persistence;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Operations.Application.Features.WorkOrders;
using Operations.Domain.Authorization;
using Operations.Domain.Enumerations;

namespace Operations.Api.Endpoints;

internal static class WorkOrderEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        group.MapPost("/flights/{flightId:guid}/work-orders", async (Guid flightId, WorkOrderRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new SubmitWorkOrderCommand(flightId, request.Type, request.ToPayload()), ct);
            return result.ToCreated(id => $"/api/v1/operations/work-orders/{id}");
        }).RequirePermission(OperationsPermissions.WorkOrders.Author).WithTags("Operations.WorkOrders");

        group.MapGet("/flights/{flightId:guid}/work-orders/mine", async (Guid flightId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetMyWorkOrderForFlightQuery(flightId), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.WorkOrders.View).WithTags("Operations.WorkOrders");

        group.MapPost("/flights/{flightId:guid}/work-orders/merge", async (Guid flightId, MergeWorkOrdersRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(request.ToCommand(flightId), ct);
            return result.ToCreated(id => $"/api/v1/operations/work-orders/{id}");
        }).RequirePermission(OperationsPermissions.WorkOrders.Merge).WithTags("Operations.WorkOrders");

        var workOrders = group.MapGroup("/work-orders").WithTags("Operations.WorkOrders");

        workOrders.MapGet("/", async (ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? search = null, Guid? stationId = null,
            WorkOrderStatus? status = null, WorkOrderType? type = null, Guid? flightId = null,
            Guid? ownerUserId = null, string? sort = null) =>
        {
            var result = await sender.Send(new GetWorkOrdersQuery(page, pageSize, search, stationId, status, type, flightId, ownerUserId, sort), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.WorkOrders.View);

        workOrders.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetWorkOrderByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.WorkOrders.View);

        workOrders.MapGet("/{id:guid}/timeline", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetWorkOrderTimelineQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.WorkOrders.View);

        workOrders.MapPut("/{id:guid}", async (Guid id, WorkOrderRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new UpdateWorkOrderCommand(id, rowVersion, request.Type, request.ToPayload()), ct);
            return result.ToNoContent();
        }).RequirePermission(OperationsPermissions.WorkOrders.Author);

        workOrders.MapDelete("/{id:guid}", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new DeleteWorkOrderCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(OperationsPermissions.WorkOrders.Author);

        workOrders.MapPost("/{id:guid}/approve", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new ApproveWorkOrderCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(OperationsPermissions.WorkOrders.Approve);

        workOrders.MapPost("/{id:guid}/return", async (Guid id, ReturnWorkOrderRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new ReturnWorkOrderCommand(id, rowVersion, request.Reason), ct);
            return result.ToNoContent();
        }).RequirePermission(OperationsPermissions.WorkOrders.Approve);
    }
}
