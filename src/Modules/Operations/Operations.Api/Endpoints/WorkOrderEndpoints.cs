using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Concurrency;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Application.Persistence;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Operations.Application.Features.WorkOrders;
using Operations.Api.Exports;
using Operations.Domain.Authorization;
using Operations.Domain.Enumerations;

namespace Operations.Api.Endpoints;

internal static class WorkOrderEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        const long MaxAttachmentUploadRequestBytes = WorkOrderAttachmentPolicy.MaxUploadBytes + 64 * 1024;
        const long MaxSignatureUploadRequestBytes = WorkOrderSignaturePolicy.MaxSignatureBytes + 64 * 1024;

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

        group.MapGet("/flights/{flightId:guid}/work-orders/approved/pdf", async (
            Guid flightId,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new GetApprovedWorkOrderPrintQuery(flightId), ct);
            if (result.IsFailure)
                return ApiResults.Problem(result.Error);

            var file = WorkOrderPrintDocumentFactory.Create(result.Value);
            return Results.File(
                file.Content,
                "application/pdf",
                file.FileName,
                enableRangeProcessing: false);
        }).RequirePermission(OperationsPermissions.WorkOrders.View)
            .WithTags("Operations.WorkOrders")
            .WithName("DownloadApprovedWorkOrder");

        group.MapPost("/flights/{flightId:guid}/work-orders/merge", async (Guid flightId, MergeWorkOrdersRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(request.ToCommand(flightId), ct);
            return result.ToCreated(id => $"/api/v1/operations/work-orders/{id}");
        }).RequirePermission(OperationsPermissions.WorkOrders.Merge).WithTags("Operations.WorkOrders");

        var workOrders = group.MapGroup("/work-orders").WithTags("Operations.WorkOrders");

        workOrders.MapPost("/from-scratch", async (CreateAdHocWorkOrderRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(request.ToCommand(), ct);
            return result.ToCreated(id => $"/api/v1/operations/work-orders/{id}");
        }).RequirePermission(OperationsPermissions.WorkOrders.Author);

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

        workOrders.MapPost("/{id:guid}/signature", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            if (!http.HasFormContentType)
                return ApiResults.Problem(BuildingBlocks.Domain.Results.Error.Validation("A multipart form with a signature file is required.", "Operations.WorkOrder.SignatureMissing"));

            var form = await http.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return ApiResults.Problem(BuildingBlocks.Domain.Results.Error.Validation("A signature file is required.", "Operations.WorkOrder.SignatureMissing"));
            if (file.Length > WorkOrderSignaturePolicy.MaxSignatureBytes)
                return ApiResults.Problem(BuildingBlocks.Domain.Results.Error.Validation("The signature image must be at most 2 MB.", "Operations.WorkOrder.SignatureTooLarge"));

            using var memory = new MemoryStream();
            await file.CopyToAsync(memory, ct);
            var result = await sender.Send(new UploadWorkOrderSignatureCommand(
                id,
                memory.ToArray(),
                file.FileName,
                file.ContentType,
                rowVersion), ct);

            return result.ToNoContent();
        }).RequirePermission(OperationsPermissions.WorkOrders.Author)
            .DisableAntiforgery()
            .WithMetadata(
                new RequestSizeLimitAttribute(MaxSignatureUploadRequestBytes),
                new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaxSignatureUploadRequestBytes });

        workOrders.MapGet("/{id:guid}/signature", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetWorkOrderSignatureContentQuery(id), ct);
            return result.IsFailure
                ? ApiResults.Problem(result.Error)
                : Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
        }).RequirePermission(OperationsPermissions.WorkOrders.View);

        workOrders.MapDelete("/{id:guid}/signature", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new DeleteWorkOrderSignatureCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(OperationsPermissions.WorkOrders.Author);

        workOrders.MapPost("/{id:guid}/tasks/{taskId:guid}/attachments", async (Guid id, Guid taskId, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            if (!http.HasFormContentType)
                return ApiResults.Problem(BuildingBlocks.Domain.Results.Error.Validation("A multipart form with an attachment file is required.", "Operations.WorkOrder.AttachmentMissing"));

            var form = await http.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return ApiResults.Problem(BuildingBlocks.Domain.Results.Error.Validation("An attachment file is required.", "Operations.WorkOrder.AttachmentMissing"));
            if (file.Length > WorkOrderAttachmentPolicy.MaxUploadBytes)
                return ApiResults.Problem(BuildingBlocks.Domain.Results.Error.Validation("The attachment file is too large.", "Operations.WorkOrder.AttachmentTooLarge"));

            var kindValue = form["kind"].FirstOrDefault();
            if (!Enum.TryParse<TaskAttachmentKind>(kindValue, ignoreCase: true, out var kind))
                return ApiResults.Problem(BuildingBlocks.Domain.Results.Error.Validation("Attachment kind is required.", "Operations.WorkOrder.AttachmentKindRequired"));

            using var memory = new MemoryStream();
            await file.CopyToAsync(memory, ct);
            var result = await sender.Send(new UploadWorkOrderTaskAttachmentCommand(
                id,
                taskId,
                kind,
                memory.ToArray(),
                file.FileName,
                file.ContentType,
                rowVersion), ct);

            return result.ToCreated(attachmentId => $"/api/v1/operations/work-orders/{id}/tasks/{taskId}/attachments/{attachmentId}");
        }).RequirePermission(OperationsPermissions.WorkOrders.Author)
            .DisableAntiforgery()
            .WithMetadata(
                new RequestSizeLimitAttribute(MaxAttachmentUploadRequestBytes),
                new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaxAttachmentUploadRequestBytes });

        workOrders.MapGet("/{id:guid}/tasks/{taskId:guid}/attachments/{attachmentId:guid}", async (Guid id, Guid taskId, Guid attachmentId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetWorkOrderTaskAttachmentContentQuery(id, taskId, attachmentId), ct);
            return result.IsFailure
                ? ApiResults.Problem(result.Error)
                : Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
        }).RequirePermission(OperationsPermissions.WorkOrders.View);

        workOrders.MapDelete("/{id:guid}/tasks/{taskId:guid}/attachments/{attachmentId:guid}", async (Guid id, Guid taskId, Guid attachmentId, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new DeleteWorkOrderTaskAttachmentCommand(id, taskId, attachmentId, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(OperationsPermissions.WorkOrders.Author);

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
