using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Concurrency;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Application.Persistence;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Features;
using System.Text.Json;
using Microsoft.Net.Http.Headers;
using Operations.Application.Contracts;
using Operations.Application.Features.Flights;
using Operations.Domain.Authorization;

namespace Operations.Api.Endpoints;

internal static class FlightEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        var flights = group.MapGroup("/flights").WithTags("Operations.Flights");

        var mobile = flights.MapGroup("/mobile");
        mobile.MapGet("/search/offline", async (ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 100) =>
        {
            var result = await sender.Send(new GetMobileFlightSearchQuery(string.Empty, page, pageSize, UnassignedIndex: true), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.View);

        mobile.MapGet("/search", async (ISender sender, CancellationToken ct, string? flightNumber = null,
            int page = 1, int pageSize = 20) =>
        {
            var result = string.IsNullOrWhiteSpace(flightNumber)
                ? await sender.Send(new GetMobileFlightHistoryQuery(page, pageSize, UsedSearchOnly: true), ct)
                : await sender.Send(new GetMobileFlightSearchQuery(flightNumber, page, pageSize), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.View);

        mobile.MapGet("/history", async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 20, string? search = null) =>
        {
            var result = await sender.Send(new GetMobileFlightHistoryQuery(page, pageSize, Search: search), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.View);

        mobile.MapGet("/adhoc/context", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetMobileAdHocFlightContextQuery(), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.Operate);

        mobile.MapPut("/adhoc/{id:guid}", async (Guid id, SaveMobileAdHocFlightDetailsRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateMobileAdHocFlightCommand(id, request), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.Operate);

        mobile.MapPut("/adhoc/{id:guid}/details", async (Guid id, SaveMobileAdHocFlightDetailsRequest request,
            HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion) return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);
            var result = await sender.Send(new UpdateMobileAdHocFlightDetailsCommand(id, request, rowVersion), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.Operate);

        mobile.MapPost("/notification-targets", async (FlightNotificationTargetsRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetFlightNotificationTargetsQuery(request.FlightIds ?? [], MobileOnly: true), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.View);

        mobile.MapGet("/", async (ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? search = null, string? sort = null, string? category = null) =>
        {
            var result = await sender.Send(new GetMobileFlightsQuery(page, pageSize, search, sort, category), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.View);

        mobile.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetMobileFlightByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.View);

        mobile.MapGet("/customer-logos/{customerId:guid}", async (Guid customerId, ISender sender, CancellationToken ct,
            string? expectedVersion = null) =>
        {
            var result = await sender.Send(new GetMobileFlightCustomerLogoQuery(customerId, expectedVersion), ct);
            return result.IsFailure
                ? ApiResults.Problem(result.Error)
                : Results.File(result.Value.Content, result.Value.ContentType, enableRangeProcessing: false,
                    entityTag: new EntityTagHeaderValue($"\"{result.Value.Version}\""));
        }).RequirePermission(OperationsPermissions.Flights.View);

        mobile.MapGet("/{id:guid}/departure-signatures/{submissionId:guid}", async (
            Guid id, Guid submissionId, DateTimeOffset expectedUpdatedAt, HttpResponse response, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetMobileFlightDepartureSignatureQuery(id, submissionId, expectedUpdatedAt), ct);
            if (result.IsFailure) return ApiResults.Problem(result.Error);
            response.Headers.CacheControl = "private, no-store";
            return Results.File(result.Value.Content, "image/png", enableRangeProcessing: false);
        }).RequirePermission(OperationsPermissions.Flights.View);

        mobile.MapGet("/{id:guid}/invitation-options", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetFlightInvitationOptionsQuery(id, MobileOnly: true), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.Invite);

        mobile.MapPost("/{id:guid}/invite", async (Guid id, AssignEmployeesRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new InviteEmployeesToFlightCommand(id, request.StaffMemberIds ?? [], rowVersion, MobileOnly: true), ct);
            return result.ToNoContent();
        }).RequirePermission(OperationsPermissions.Flights.Invite);

        mobile.MapGet("/{id:guid}/tasks/{recordId:guid}/attachments/{attachmentId:guid}", async (
            Guid id, Guid recordId, Guid attachmentId, DateTimeOffset expectedUploadedAt, HttpResponse response, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetMobileFlightTaskAttachmentQuery(id, recordId, attachmentId, expectedUploadedAt), ct);
            if (result.IsFailure) return ApiResults.Problem(result.Error);
            response.Headers.CacheControl = "private, no-store";
            return Results.File(result.Value.Content, result.Value.ContentType, fileDownloadName: result.Value.FileName, enableRangeProcessing: false);
        }).RequirePermission(OperationsPermissions.Flights.View);

        mobile.MapGet("/{id:guid}/services/{recordId:guid}/attachments/{attachmentId:guid}", async (
            Guid id, Guid recordId, Guid attachmentId, DateTimeOffset expectedUploadedAt, HttpResponse response, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetMobileFlightServiceAttachmentQuery(id, recordId, attachmentId, expectedUploadedAt), ct);
            if (result.IsFailure) return ApiResults.Problem(result.Error);
            response.Headers.CacheControl = "private, no-store";
            return Results.File(result.Value.Content, result.Value.ContentType, fileDownloadName: result.Value.FileName, enableRangeProcessing: false);
        }).RequirePermission(OperationsPermissions.Flights.View);

        flights.MapPut("/{id:guid}/operations-batch", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
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
                return ApiResults.Problem(BuildingBlocks.Domain.Results.Error.Validation("The flight edit is invalid.", "Operations.FlightBatch.RequestInvalid"));
            }
            catch (BadHttpRequestException error) { return Results.StatusCode(error.StatusCode); }
            if (request is null)
                return ApiResults.Problem(BuildingBlocks.Domain.Results.Error.Validation("A flight edit is required.", "Operations.FlightBatch.RequestInvalid"));
            var result = await sender.Send(new SaveFlightOperationsBatchCommand(id, request, rowVersion), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.Operate)
            .WithMetadata(new RequestSizeLimitAttribute(FlightOperationsBatchPolicy.MaxJsonRequestBytes));

        flights.MapPut("/{id:guid}/services/{recordId:guid}", async (Guid id, Guid recordId, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);
            // Configure the server before JSON is read: one valid 25 MB voice file exceeds
            // Kestrel's default body cap after base64 encoding. Keep the aggregate bounded.
            var bodyLimit = http.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = FlightServiceAttachmentPolicy.MaxJsonRequestBytes;
            if (http.ContentLength > FlightServiceAttachmentPolicy.MaxJsonRequestBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            if (!http.HasJsonContentType()) return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
            SaveFlightServiceRequest? request;
            try { request = await http.ReadFromJsonAsync<SaveFlightServiceRequest>(ct); }
            catch (JsonException)
            {
                return ApiResults.Problem(BuildingBlocks.Domain.Results.Error.Validation("The service input is invalid.", "Operations.FlightService.RequestInvalid"));
            }
            catch (BadHttpRequestException error)
            {
                return Results.StatusCode(error.StatusCode);
            }
            if (request is null)
                return ApiResults.Problem(BuildingBlocks.Domain.Results.Error.Validation("Service input is required.", "Operations.FlightService.RequestInvalid"));
            var result = await sender.Send(new SaveFlightServiceCommand(id, recordId, request, rowVersion), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.Operate)
            .WithMetadata(new RequestSizeLimitAttribute(FlightServiceAttachmentPolicy.MaxJsonRequestBytes));

        flights.MapDelete("/{id:guid}/services/{recordId:guid}", async (Guid id, Guid recordId, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);
            var result = await sender.Send(new DeleteFlightServiceCommand(id, recordId, rowVersion), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.Operate);

        flights.MapPut("/{id:guid}/tasks/{recordId:guid}", async (Guid id, Guid recordId, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);
            // Configure the server before JSON is read: one valid 25 MB voice file exceeds
            // Kestrel's default body cap after base64 encoding. Keep the aggregate bounded.
            var bodyLimit = http.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = FlightServiceAttachmentPolicy.MaxJsonRequestBytes;
            if (http.ContentLength > FlightServiceAttachmentPolicy.MaxJsonRequestBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            if (!http.HasJsonContentType()) return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
            SaveFlightTaskRequest? request;
            try { request = await http.ReadFromJsonAsync<SaveFlightTaskRequest>(ct); }
            catch (JsonException)
            {
                return ApiResults.Problem(BuildingBlocks.Domain.Results.Error.Validation("The task input is invalid.", "Operations.FlightTask.RequestInvalid"));
            }
            catch (BadHttpRequestException error)
            {
                return Results.StatusCode(error.StatusCode);
            }
            if (request is null)
                return ApiResults.Problem(BuildingBlocks.Domain.Results.Error.Validation("Task input is required.", "Operations.FlightTask.RequestInvalid"));
            var result = await sender.Send(new SaveFlightTaskCommand(id, recordId, request, rowVersion), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.Operate)
            .WithMetadata(new RequestSizeLimitAttribute(FlightServiceAttachmentPolicy.MaxJsonRequestBytes));

        flights.MapDelete("/{id:guid}/tasks/{recordId:guid}", async (Guid id, Guid recordId, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);
            var result = await sender.Send(new DeleteFlightTaskCommand(id, recordId, rowVersion), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.Operate);

        flights.MapPut("/{id:guid}/arrival", async (Guid id, SaveFlightArrivalRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);
            var result = await sender.Send(new SaveFlightArrivalCommand(id, request, rowVersion), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.Operate);

        flights.MapPut("/{id:guid}/departure", async (Guid id, SaveFlightDepartureRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);
            var result = await sender.Send(new SaveFlightDepartureCommand(id, request, rowVersion), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.Operate);

        flights.MapPut("/{id:guid}/cancellation", async (Guid id, SaveFlightCancellationRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);
            var result = await sender.Send(new SaveFlightCancellationCommand(id, request, rowVersion), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.Operate);

        flights.MapDelete("/{id:guid}/cancellation/{submissionId:guid}", async (Guid id, Guid submissionId, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);
            var result = await sender.Send(new RevertFlightCancellationCommand(id, submissionId, rowVersion), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.Operate);

        flights.MapPost("/notification-targets", async (FlightNotificationTargetsRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetFlightNotificationTargetsQuery(request.FlightIds ?? []), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.View);

        flights.MapGet("/", async (ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? search = null, Guid? stationId = null, Guid? customerId = null,
            Guid? operationTypeId = null, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, string? sort = null) =>
        {
            var result = await sender.Send(new GetFlightsQuery(
                page, pageSize, search, stationId, customerId, operationTypeId, fromUtc, toUtc, sort), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.View);

        flights.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetFlightByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.View);

        flights.MapGet("/customer-logos/{customerId:guid}", async (Guid customerId, ISender sender, CancellationToken ct,
            string? expectedVersion = null) =>
        {
            var result = await sender.Send(new GetFlightCustomerLogoQuery(customerId, expectedVersion), ct);
            return result.IsFailure
                ? ApiResults.Problem(result.Error)
                : Results.File(result.Value.Content, result.Value.ContentType, enableRangeProcessing: false,
                    entityTag: new EntityTagHeaderValue($"\"{result.Value.Version}\""));
        }).RequirePermission(OperationsPermissions.Flights.View);

        flights.MapPost("/", async (ScheduleFlightRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ScheduleFlightCommand(
                request.CustomerId, request.StationId, request.OperationTypeId, request.FlightNumber,
                request.ScheduledArrivalUtc, request.ScheduledDepartureUtc, request.AircraftTypeId,
                request.PlannedServiceIds ?? [], request.AssignedStaffMemberIds ?? []), ct);
            return result.ToCreated(id => $"/api/v1/operations/flights/{id}");
        }).RequirePermission(OperationsPermissions.Flights.Schedule);

        flights.MapPost("/bulk", async (ScheduleFlightsRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ScheduleFlightsCommand(
                request.CustomerId, request.StationId, request.OperationTypeId, request.FlightNumber,
                request.ScheduledArrivalTimeUtc, request.ScheduledDepartureTimeUtc, request.SelectedDates ?? [],
                request.AircraftTypeId, request.PlannedServiceIds ?? [], request.AssignedStaffMemberIds ?? [],
                request.TimeZoneId), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.Schedule);

        flights.MapPut("/{id:guid}", async (Guid id, UpdateScheduledFlightRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new UpdateScheduledFlightCommand(
                id, request.CustomerId, request.StationId, request.OperationTypeId, request.ScheduledArrivalUtc,
                request.ScheduledDepartureUtc, request.AircraftTypeId, request.PlannedServiceIds ?? [], rowVersion,
                request.AssignedStaffMemberIds), ct);
            return result.ToNoContent();
        }).RequirePermission(OperationsPermissions.Flights.Update);

        flights.MapPost("/{id:guid}/change-number", async (Guid id, ChangeFlightNumberRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new ChangeFlightNumberCommand(id, request.FlightNumber, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(OperationsPermissions.Flights.Update);

        flights.MapPost("/{id:guid}/assign", async (Guid id, AssignEmployeesRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new AssignEmployeesCommand(id, request.StaffMemberIds ?? [], rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(OperationsPermissions.Flights.Assign);

        flights.MapGet("/{id:guid}/invitation-options", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetFlightInvitationOptionsQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.Invite);

        flights.MapPost("/{id:guid}/invite", async (Guid id, AssignEmployeesRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new InviteEmployeesToFlightCommand(id, request.StaffMemberIds ?? [], rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(OperationsPermissions.Flights.Invite);
    }
}
