using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Concurrency;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Features;
using System.Text.Json;
using Microsoft.Net.Http.Headers;
using Operations.Api.Exports;
using Operations.Application.Contracts;
using Operations.Application.Features.Flights;
using Operations.Domain.Authorization;

namespace Operations.Api.Endpoints;

internal static class FlightEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        var flights = group.MapGroup("/flights").WithTags("Operations.Flights");
        PortalFlightOperationEndpoints.Map(flights);

        var mobile = flights.MapGroup("/mobile");
        mobile.MapPut("/{flightId:guid}/shares/{shareId:guid}", async (Guid flightId, Guid shareId,
            PublishFlightShareRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new PublishFlightShareCommand(flightId, shareId, request), ct)).ToOk())
            .RequirePermission(OperationsPermissions.Flights.Operate);

        mobile.MapGet("/shares", async (ISender sender, CancellationToken ct, string direction = "all",
            string? search = null, int page = 1, int pageSize = 20) =>
            (await sender.Send(new GetMobileFlightSharesQuery(direction, search, page, pageSize), ct)).ToOk())
            .RequirePermission(OperationsPermissions.Flights.View);

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

        mobile.MapPost("/details/batch", async (MobileFlightDetailsBatchRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetMobileFlightDetailsBatchQuery(request.FlightIds ?? []), ct)).ToOk())
            .RequirePermission(OperationsPermissions.Flights.View);

        mobile.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetMobileFlightByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.View);

        mobile.MapGet("/{id:guid}/print", (Guid id, HttpResponse response, ISender sender, CancellationToken ct) =>
            PortalFlightOperationEndpoints.DownloadWorkOrderAsync(id, response, sender, ct, mobileOnly: true))
            .RequirePermission(OperationsPermissions.Flights.View);

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

        mobile.MapGet("/{id:guid}/returns-to-ramp/{occurrenceId:guid}/signature", async (
            Guid id, Guid occurrenceId, DateTimeOffset expectedUpdatedAt, HttpResponse response, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetMobileFlightReturnToRampSignatureQuery(id, occurrenceId, expectedUpdatedAt), ct);
            if (result.IsFailure) return ApiResults.Problem(result.Error);
            response.Headers.CacheControl = "private, no-store";
            return Results.File(result.Value.Content, "image/png", enableRangeProcessing: false);
        }).RequirePermission(OperationsPermissions.Flights.View);

        flights.MapPut("/{id:guid}/returns-to-ramp/{occurrenceId:guid}", (Guid id, Guid occurrenceId, HttpRequest http, ISender sender, CancellationToken ct) =>
            PortalFlightOperationEndpoints.SaveReturnToRampAsync(id, occurrenceId, http, sender, ct, portalOnly: false))
            .RequirePermission(OperationsPermissions.Flights.Operate)
            .WithMetadata(new RequestSizeLimitAttribute(FlightOperationsBatchPolicy.MaxJsonRequestBytes));

        flights.MapPut("/{id:guid}/operations-batch", (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
            PortalFlightOperationEndpoints.SaveBatchAsync(id, http, sender, ct, portalOnly: false))
            .RequirePermission(OperationsPermissions.Flights.Operate)
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
            Guid? operationTypeId = null, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, string? sort = null,
            string? status = null, string? flightType = null, bool? hasOperationalRecords = null, string? workOrderStatus = null) =>
        {
            var statuses = ParseStatuses(status);
            if (statuses is null)
                return ApiResults.Problem(Error.Validation("One or more flight statuses are invalid.", "Operations.Flight.StatusInvalid"));

            if (!TryParseFlightType(flightType, out var type))
                return ApiResults.Problem(Error.Validation("Choose All, Scheduled, PerLanding, OnCall, or AdHoc.", "Operations.Flight.TypeInvalid"));
            if (!TryParseWorkOrderStatus(workOrderStatus, out var orderStatus))
                return ApiResults.Problem(WorkOrderStatusInvalid());

            var result = await sender.Send(new GetFlightsQuery(
                page, pageSize, search, stationId, customerId, operationTypeId, fromUtc, toUtc, sort, statuses, type,
                hasOperationalRecords, orderStatus), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.View);

        flights.MapGet("/calendar", async (ISender sender, CancellationToken ct, DateTimeOffset fromUtc, DateTimeOffset toUtc,
            Guid? stationId = null, Guid? customerId = null) =>
        {
            var result = await sender.Send(new GetFlightCalendarQuery(fromUtc, toUtc, stationId, customerId), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.View);

        flights.MapGet("/export", ExportFlightsAsync)
            .RequirePermission(OperationsPermissions.Flights.View)
            .RequirePermission(OperationsPermissions.Flights.Export)
            .WithName("ExportFlights");

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

    private static async Task<IResult> ExportFlightsAsync(ISender sender,
        [FromServices] ILegacySystemIdReader legacySystemIds, TimeProvider clock, HttpResponse response, CancellationToken ct,
        string? format = null, string? search = null, Guid? stationId = null, Guid? customerId = null,
        Guid? operationTypeId = null, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, string? sort = null,
        string? status = null, string? flightType = null, string? timeZoneId = null, string? report = null,
        bool? hasOperationalRecords = null, string? workOrderStatus = null)
    {
        if (!FlightExportDocumentFactory.TryParseFormat(format, out var exportFormat))
            return ApiResults.Problem(Error.Validation(
                new Dictionary<string, string[]> { ["format"] = ["Format must be one of: xlsx, csv, or pdf."] },
                code: "Operations.Flight.ExportFormatInvalid"));

        var billing = string.Equals(report, "billing", StringComparison.OrdinalIgnoreCase);
        if ((!string.IsNullOrWhiteSpace(report) && !billing) || (billing && exportFormat != FlightExportFormat.Xlsx))
            return ApiResults.Problem(Error.Validation(
                "The billing report requires Excel format (xlsx). Omit report for standard exports.",
                "Operations.Flight.ExportReportInvalid"));

        if (!FlightExportTimeZoneResolver.TryResolve(timeZoneId, out var timeZone))
            return ApiResults.Problem(Error.Validation(new Dictionary<string, string[]>
            {
                ["timeZoneId"] = ["Time zone must be a valid IANA, Windows, or Browser UTC±HH:mm identifier."]
            }, code: "Operations.Flight.ExportTimeZoneInvalid"));

        var statuses = ParseStatuses(status);
        if (statuses is null)
            return ApiResults.Problem(Error.Validation("One or more flight statuses are invalid.", "Operations.Flight.StatusInvalid"));
        if (!TryParseFlightType(flightType, out var type))
            return ApiResults.Problem(Error.Validation("Choose All, Scheduled, PerLanding, OnCall, or AdHoc.", "Operations.Flight.TypeInvalid"));
        if (!TryParseWorkOrderStatus(workOrderStatus, out var orderStatus))
            return ApiResults.Problem(WorkOrderStatusInvalid());

        var result = await sender.Send(new GetFlightsExportQuery(search, stationId, customerId, operationTypeId,
            fromUtc, toUtc, sort, statuses, type, IncludeManpower: billing, HasOperationalRecords: hasOperationalRecords,
            WorkOrderStatus: orderStatus), ct);
        if (result.IsFailure)
            return ApiResults.Problem(result.Error);

        if (string.IsNullOrWhiteSpace(timeZoneId) && stationId is { } filterStation &&
            result.Value.FirstOrDefault(row => row.StationId == filterStation) is { } stationRow &&
            FlightExportTimeZoneResolver.TryResolve(stationRow.StationTimeZoneId, out var stationZone))
            timeZone = stationZone;

        var generatedAtUtc = clock.GetUtcNow();
        FlightExportFile file;
        if (billing)
        {
            file = BillingExportDocumentFactory.Create(result.Value, generatedAtUtc, timeZone);
        }
        else
        {
            var legacyIds = exportFormat == FlightExportFormat.Xlsx
                ? await legacySystemIds.GetAsync(FlightExportDocumentFactory.CollectLegacySystemIdRequest(result.Value), ct)
                : null;
            var criteria = new FlightExportCriteria(search, stationId is { } station ? [station] : [],
                customerId is { } customer ? [customer] : [], operationTypeId, statuses, fromUtc, toUtc, type, orderStatus);
            file = FlightExportDocumentFactory.Create(exportFormat, result.Value, criteria, generatedAtUtc, timeZone, legacyIds);
        }

        response.Headers.CacheControl = "private, no-store";
        return Results.File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: false);
    }

    private static bool TryParseFlightType(string? value, out FlightListType type)
    {
        type = FlightListType.All;
        return string.IsNullOrWhiteSpace(value) ||
            Enum.TryParse(value, ignoreCase: true, out type) &&
            string.Equals(Enum.GetName(type), value.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseWorkOrderStatus(string? value, out WorkOrderStatusFilter? status)
    {
        status = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;
        if (!Enum.TryParse<WorkOrderStatusFilter>(value, ignoreCase: true, out var parsed) ||
            !string.Equals(Enum.GetName(parsed), value.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        status = parsed;
        return true;
    }

    private static Error WorkOrderStatusInvalid() =>
        Error.Validation("Choose Not Approved, Approved, Unlocked, or Conflict.", "Operations.Flight.WorkOrderStatusInvalid");

    private static IReadOnlyList<FlightReportStatus>? ParseStatuses(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var statuses = new List<FlightReportStatus>();
        foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<FlightReportStatus>(item, ignoreCase: true, out var status)
                || !Enum.IsDefined(status) || int.TryParse(item, out _))
                return null;

            if (!statuses.Contains(status))
                statuses.Add(status);
        }
        return statuses;
    }
}
