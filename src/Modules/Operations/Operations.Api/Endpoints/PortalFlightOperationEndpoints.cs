using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Concurrency;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;
using Operations.Application.Contracts;
using Operations.Application.Features.Flights;
using Operations.Domain.Authorization;
using System.Text.Json;

namespace Operations.Api.Endpoints;

internal static class PortalFlightOperationEndpoints
{
    internal static void Map(RouteGroupBuilder flights)
    {
        var operations = flights.MapGroup("/{id:guid}/operations")
            .RequirePermission(OperationsPermissions.Flights.View);
        var writes = operations.MapGroup("").RequirePermission(OperationsPermissions.Flights.Operate);

        operations.MapGet("", async (Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetPortalFlightOperationsQuery(id), ct)).ToOk());

        operations.MapGet("/print", (Guid id, HttpResponse response, ISender sender, IFlightWorkOrderPdfRenderer renderer, CancellationToken ct) =>
            DownloadWorkOrderAsync(id, response, sender, renderer, ct, mobileOnly: false));

        operations.MapPut("/approval", (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
            SetApprovalAsync(id, true, http, sender, ct));

        operations.MapDelete("/approval", (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
            SetApprovalAsync(id, false, http, sender, ct));

        writes.MapDelete("/work-order", (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
            DeleteOrderAsync(id, FlightOrderKind.Work, http, sender, ct));

        writes.MapDelete("/cancellation-order", (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
            DeleteOrderAsync(id, FlightOrderKind.Cancellation, http, sender, ct));

        writes.MapPut("/batch", (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
            SaveBatchAsync(id, http, sender, ct, portalOnly: true))
            .WithMetadata(new RequestSizeLimitAttribute(FlightOperationsBatchPolicy.MaxJsonRequestBytes));

        writes.MapPut("/returns-to-ramp/{occurrenceId:guid}", (Guid id, Guid occurrenceId, HttpRequest http, ISender sender, CancellationToken ct) =>
            SaveReturnToRampAsync(id, occurrenceId, http, sender, ct, portalOnly: true))
            .WithMetadata(new RequestSizeLimitAttribute(FlightOperationsBatchPolicy.MaxJsonRequestBytes));

        writes.MapPut("/cancellation", async (Guid id, SaveFlightCancellationRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);
            return (await sender.Send(new SaveFlightCancellationCommand(id, request, rowVersion, PortalOnly: true), ct)).ToOk();
        });

        writes.MapPut("/cancellation/{submissionId:guid}", async (
            Guid id, Guid submissionId, SaveFlightCancellationRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);
            return (await sender.Send(new SaveFlightCancellationCommand(id, request, rowVersion, PortalOnly: true,
                TargetSubmissionId: submissionId), ct)).ToOk();
        });

        writes.MapDelete("/cancellation/{submissionId:guid}", async (Guid id, Guid submissionId, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);
            return (await sender.Send(new RevertFlightCancellationCommand(id, submissionId, rowVersion, PortalOnly: true), ct)).ToOk();
        });

        operations.MapGet("/departure-signatures/{submissionId:guid}", async (
            Guid id, Guid submissionId, DateTimeOffset expectedUpdatedAt, HttpResponse response, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetMobileFlightDepartureSignatureQuery(id, submissionId, expectedUpdatedAt, PortalOnly: true), ct);
            return result.IsFailure ? ApiResults.Problem(result.Error) : PrivateFile(response, result.Value.Content, "image/png");
        });

        operations.MapGet("/returns-to-ramp/{occurrenceId:guid}/signature", async (
            Guid id, Guid occurrenceId, DateTimeOffset expectedUpdatedAt, HttpResponse response, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetMobileFlightReturnToRampSignatureQuery(id, occurrenceId, expectedUpdatedAt, PortalOnly: true), ct);
            return result.IsFailure ? ApiResults.Problem(result.Error) : PrivateFile(response, result.Value.Content, "image/png");
        });

        operations.MapGet("/services/{recordId:guid}/attachments/{attachmentId:guid}", async (
            Guid id, Guid recordId, Guid attachmentId, DateTimeOffset expectedUploadedAt, HttpResponse response, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetMobileFlightServiceAttachmentQuery(id, recordId, attachmentId, expectedUploadedAt, PortalOnly: true), ct);
            return result.IsFailure ? ApiResults.Problem(result.Error) :
                PrivateFile(response, result.Value.Content, result.Value.ContentType, result.Value.FileName);
        });

        operations.MapGet("/tasks/{recordId:guid}/attachments/{attachmentId:guid}", async (
            Guid id, Guid recordId, Guid attachmentId, DateTimeOffset expectedUploadedAt, HttpResponse response, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetMobileFlightTaskAttachmentQuery(id, recordId, attachmentId, expectedUploadedAt, PortalOnly: true), ct);
            return result.IsFailure ? ApiResults.Problem(result.Error) :
                PrivateFile(response, result.Value.Content, result.Value.ContentType, result.Value.FileName);
        });
    }

    internal static async Task<IResult> DownloadWorkOrderAsync(Guid id, HttpResponse response, ISender sender,
        IFlightWorkOrderPdfRenderer renderer, CancellationToken ct, bool mobileOnly)
    {
        var result = await sender.Send(new GetFlightWorkOrderPrintQuery(id, MobileOnly: mobileOnly), ct);
        if (result.IsFailure)
            return ApiResults.Problem(result.Error);

        var file = renderer.Render(result.Value);
        var disposition = new ContentDispositionHeaderValue("inline");
        disposition.SetHttpFileName(file.FileName);
        response.Headers.ContentDisposition = disposition.ToString();
        response.Headers.CacheControl = "private, no-store";
        return Results.File(file.Content, "application/pdf");
    }

    private static async Task<IResult> SetApprovalAsync(Guid id, bool approve, HttpRequest http, ISender sender, CancellationToken ct)
    {
        if (http.GetIfMatch() is not { } rowVersion)
            return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

        var result = await sender.Send(new SetFlightApprovalCommand(id, approve, rowVersion), ct);
        return result.IsFailure ? ApiResults.Problem(result.Error) : (await sender.Send(new GetPortalFlightOperationsQuery(id), ct)).ToOk();
    }

    private static async Task<IResult> DeleteOrderAsync(Guid id, FlightOrderKind kind, HttpRequest http, ISender sender, CancellationToken ct)
    {
        if (http.GetIfMatch() is not { } rowVersion)
            return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

        return (await sender.Send(new DeletePortalFlightOrderCommand(id, kind, rowVersion), ct)).ToNoContent();
    }

    private static IResult PrivateFile(HttpResponse response, Stream content, string contentType, string? fileName = null)
    {
        response.Headers.CacheControl = "private, no-store";
        return Results.File(content, contentType, fileDownloadName: fileName, enableRangeProcessing: false);
    }

    internal static async Task<IResult> SaveReturnToRampAsync(Guid id, Guid occurrenceId, HttpRequest http, ISender sender, CancellationToken ct, bool portalOnly)
    {
        if (http.GetIfMatch() is not { } rowVersion) return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);
        var bodyLimit = http.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = FlightOperationsBatchPolicy.MaxJsonRequestBytes;
        if (http.ContentLength > FlightOperationsBatchPolicy.MaxJsonRequestBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        if (!http.HasJsonContentType()) return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        SaveFlightReturnToRampRequest? request;
        try { request = await http.ReadFromJsonAsync<SaveFlightReturnToRampRequest>(ct); }
        catch (JsonException) { return ApiResults.Problem(Error.Validation("The RTR input is invalid.", "Operations.ReturnToRamp.RequestInvalid")); }
        catch (BadHttpRequestException error) { return Results.StatusCode(error.StatusCode); }
        if (request is null) return ApiResults.Problem(Error.Validation("RTR input is required.", "Operations.ReturnToRamp.RequestInvalid"));
        return (await sender.Send(new SaveFlightReturnToRampCommand(id, occurrenceId, request, rowVersion, PortalOnly: portalOnly), ct)).ToOk();
    }

    internal static async Task<IResult> SaveBatchAsync(Guid id, HttpRequest http, ISender sender, CancellationToken ct, bool portalOnly)
    {
        if (http.GetIfMatch() is not { } rowVersion)
            return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);
        var bodyLimit = http.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = FlightOperationsBatchPolicy.MaxJsonRequestBytes;
        if (http.ContentLength > FlightOperationsBatchPolicy.MaxJsonRequestBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        if (!http.HasJsonContentType()) return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        SaveFlightOperationsBatchRequest? request;
        try { request = await http.ReadFromJsonAsync<SaveFlightOperationsBatchRequest>(ct); }
        catch (JsonException)
        {
            return ApiResults.Problem(Error.Validation("The flight edit is invalid.", "Operations.FlightBatch.RequestInvalid"));
        }
        catch (BadHttpRequestException error) { return Results.StatusCode(error.StatusCode); }
        if (request is null)
            return ApiResults.Problem(Error.Validation("A flight edit is required.", "Operations.FlightBatch.RequestInvalid"));
        var result = await sender.Send(new SaveFlightOperationsBatchCommand(id, request, rowVersion, PortalOnly: portalOnly), ct);
        return result.ToOk();
    }
}
