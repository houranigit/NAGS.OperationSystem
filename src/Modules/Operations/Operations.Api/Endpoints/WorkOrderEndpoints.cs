using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Concurrency;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Application.Persistence;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Operations.Application.Features.Merge;
using Operations.Application.Features.WorkOrders;
using Operations.Domain.Authorization;

namespace Operations.Api.Endpoints;

internal static class WorkOrderEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        var workOrders = group.MapGroup("/work-orders").WithTags("Operations.WorkOrders");

        workOrders.MapGet("/review-queue", async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 20, Guid? stationId = null) =>
        {
            var result = await sender.Send(new GetReviewQueueQuery(page, pageSize, stationId), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.WorkOrders.View);

        workOrders.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetWorkOrderByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.WorkOrders.View);

        // Open a completion work order for a scheduled flight.
        group.MapPost("/flights/{flightId:guid}/work-orders", async (Guid flightId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new OpenWorkOrderCommand(flightId), ct);
            return result.ToCreated(id => $"/api/v1/operations/work-orders/{id}");
        }).RequirePermission(OperationsPermissions.WorkOrders.Author).WithTags("Operations.WorkOrders");

        workOrders.MapPut("/{id:guid}", async (Guid id, UpdateWorkOrderRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new UpdateWorkOrderCommand(
                id, request.ServiceLines ?? [], request.Tasks ?? [], request.ActualFlightNumber, request.ActualAircraftTypeId,
                request.ActualArrivalUtc, request.ActualDepartureUtc,
                request.AircraftTailNumber, request.Remarks, request.CustomerSignatureReference, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(OperationsPermissions.WorkOrders.Author);

        workOrders.MapPost("/{id:guid}/submit", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new SubmitWorkOrderCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(OperationsPermissions.WorkOrders.Submit);

        workOrders.MapPost("/{id:guid}/approve", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new ApproveWorkOrderCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(OperationsPermissions.WorkOrders.Approve);

        workOrders.MapPost("/{id:guid}/reject", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new RejectWorkOrderCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(OperationsPermissions.WorkOrders.Reject);

        workOrders.MapPost("/{id:guid}/return", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new ReturnWorkOrderToReviewCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(OperationsPermissions.WorkOrders.Return);

        workOrders.MapPost("/merge", async (MergeWorkOrdersRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new MergeDuplicateWorkOrdersCommand(request.SurvivorWorkOrderId, request.LoserWorkOrderId), ct);
            return result.ToNoContent();
        }).RequirePermission(OperationsPermissions.WorkOrders.Merge);
    }
}
