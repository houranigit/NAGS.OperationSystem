using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Concurrency;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Operations.Application.Features.Dashboard;
using Operations.Application.Features.Flights;
using Operations.Application.Features.Merge;
using Operations.Application.Features.WorkOrders;
using Operations.Api.Exports;
using Operations.Domain.Authorization;
using Operations.Domain.Enumerations;

namespace Operations.Api.Endpoints;

internal static class FlightEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        var flights = group.MapGroup("/flights").WithTags("Operations.Flights");

        flights.MapGet("/", async (ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? search = null, Guid? stationId = null, Guid? customerId = null,
            Guid? operationTypeId = null, string? status = null, string? serviceCategory = null,
            DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, string? sort = null) =>
        {
            var statuses = ParseStatuses(status);
            if (statuses is null)
                return ApiResults.Problem(Error.Validation("One or more flight statuses are invalid.", "Operations.Flight.StatusInvalid"));
            var serviceCategories = ParseServiceCategories(serviceCategory);
            if (serviceCategories is null)
                return ApiResults.Problem(Error.Validation("One or more flight service categories are invalid.", "Operations.Flight.ServiceCategoryInvalid"));
            var result = await sender.Send(new GetFlightsQuery(
                page, pageSize, search, stationId, customerId, operationTypeId, statuses, fromUtc, toUtc, serviceCategories, sort), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.View);

        flights.MapGet("/export", async (
            string format,
            ISender sender,
            TimeProvider timeProvider,
            CancellationToken ct,
            string? search = null,
            Guid? stationId = null,
            Guid? customerId = null,
            Guid? operationTypeId = null,
            string? status = null,
            string? serviceCategory = null,
            DateTimeOffset? fromUtc = null,
            DateTimeOffset? toUtc = null,
            string? sort = null) =>
        {
            if (!FlightExportDocumentFactory.TryParseFormat(format, out var exportFormat))
            {
                return ApiResults.Problem(Error.Validation(
                    new Dictionary<string, string[]>
                    {
                        ["format"] = ["Format must be one of: xlsx, csv, or pdf."]
                    },
                    code: "Operations.Flight.ExportFormatInvalid"));
            }

            var statuses = ParseStatuses(status);
            if (statuses is null)
                return ApiResults.Problem(Error.Validation("One or more flight statuses are invalid.", "Operations.Flight.StatusInvalid"));
            var serviceCategories = ParseServiceCategories(serviceCategory);
            if (serviceCategories is null)
                return ApiResults.Problem(Error.Validation("One or more flight service categories are invalid.", "Operations.Flight.ServiceCategoryInvalid"));

            var result = await sender.Send(new GetFlightsExportQuery(
                search,
                stationId,
                customerId,
                operationTypeId,
                statuses,
                fromUtc,
                toUtc,
                serviceCategories,
                sort), ct);

            if (result.IsFailure)
                return ApiResults.Problem(result.Error);

            var file = FlightExportDocumentFactory.Create(
                exportFormat,
                result.Value,
                new FlightExportCriteria(
                    search,
                    stationId,
                    customerId,
                    operationTypeId,
                    statuses,
                    fromUtc,
                    toUtc,
                    serviceCategories,
                    sort),
                timeProvider.GetUtcNow());

            return Results.File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: false);
        }).RequirePermission(OperationsPermissions.Flights.View)
            .RequirePermission(OperationsPermissions.Flights.Export)
            .WithName("ExportFlights");

        flights.MapGet("/per-landing-extract", async (
            ISender sender,
            CancellationToken ct,
            string? search = null,
            Guid? stationId = null,
            Guid? customerId = null,
            Guid? operationTypeId = null,
            string? status = null,
            string? serviceCategory = null,
            DateTimeOffset? fromUtc = null,
            DateTimeOffset? toUtc = null,
            string? sort = null) =>
        {
            var statuses = ParseStatuses(status);
            if (statuses is null)
                return ApiResults.Problem(Error.Validation("One or more flight statuses are invalid.", "Operations.Flight.StatusInvalid"));
            var serviceCategories = ParseServiceCategories(serviceCategory);
            if (serviceCategories is null)
                return ApiResults.Problem(Error.Validation("One or more flight service categories are invalid.", "Operations.Flight.ServiceCategoryInvalid"));

            var result = await sender.Send(new GetPerLandingExtractionQuery(
                search,
                stationId,
                customerId,
                operationTypeId,
                statuses,
                fromUtc,
                toUtc,
                serviceCategories,
                sort), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.View)
            .RequirePermission(OperationsPermissions.WorkOrders.Approve);

        flights.MapPost("/per-landing-extract/approve", async (
            ApprovePerLandingFlightsRequest request,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(request.ToCommand(), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.WorkOrders.Approve);

        flights.MapGet("/calendar", async (ISender sender, CancellationToken ct,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            Guid? stationId = null,
            Guid? customerId = null,
            FlightStatus? status = null) =>
        {
            var result = await sender.Send(new GetSchedulerCalendarQuery(fromUtc, toUtc, stationId, customerId, status), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.View);

        flights.MapGet("/duplicate-candidates", async (ISender sender, CancellationToken ct,
            Guid customerId, DateTimeOffset scheduledArrivalUtc, DateTimeOffset scheduledDepartureUtc, Guid? stationId = null, Guid? excludeFlightId = null) =>
        {
            var result = await sender.Send(new FindDuplicateCandidatesQuery(customerId, stationId, scheduledArrivalUtc, scheduledDepartureUtc, excludeFlightId), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.View);

        flights.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetFlightByIdQuery(id), ct);
            return result.ToOk();
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
                request.AircraftTypeId, request.PlannedServiceIds ?? [], request.AssignedStaffMemberIds ?? []), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.Schedule);

        flights.MapGet("/{id:guid}/timeline", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetFlightTimelineQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.View);

        flights.MapGet("/{id:guid}/invite-options", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetFlightInviteOptionsQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.Invite);

        flights.MapPut("/{id:guid}", async (Guid id, UpdateScheduledFlightRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new UpdateScheduledFlightCommand(
                id, request.CustomerId, request.StationId, request.OperationTypeId, request.ScheduledArrivalUtc,
                request.ScheduledDepartureUtc, request.AircraftTypeId, request.PlannedServiceIds ?? [], rowVersion), ct);
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

        flights.MapPost("/{id:guid}/invite", async (Guid id, AssignEmployeesRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new InviteEmployeesToFlightCommand(id, request.StaffMemberIds ?? [], rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(OperationsPermissions.Flights.Invite);

        flights.MapPost("/{id:guid}/claim", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new ClaimPerLandingFlightCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(OperationsPermissions.Flights.Assign);

        flights.MapPost("/merge", async (MergeFlightsRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new MergeDuplicateFlightsCommand(request.SurvivorFlightId, request.LoserFlightId), ct);
            return result.ToNoContent();
        }).RequirePermission(OperationsPermissions.Flights.Merge);

        // Keep the lightweight home-page summary behind its existing permission. The detailed
        // analytics page has a separate permission because it exposes flight-level records/export.
        group.MapGet("/dashboard", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetOperationsDashboardQuery(
                IncludeAnalytics: false,
                IncludeOptions: false), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Dashboard.View).WithTags("Operations.Dashboard");

        group.MapGet("/analytics-dashboard", async (
            ISender sender,
            CancellationToken ct,
            DateTimeOffset? fromUtc = null,
            DateTimeOffset? toUtc = null,
            string? stationIds = null,
            string? customerIds = null,
            string? serviceIds = null,
            int topCount = 5,
            bool includeAnalytics = true,
            bool includeOptions = true) =>
        {
            if (!TryParseDashboardIds(stationIds, out var parsedStationIds))
                return ApiResults.Problem(InvalidDashboardIds("stationIds"));
            if (!TryParseDashboardIds(customerIds, out var parsedCustomerIds))
                return ApiResults.Problem(InvalidDashboardIds("customerIds"));
            if (!TryParseDashboardIds(serviceIds, out var parsedServiceIds))
                return ApiResults.Problem(InvalidDashboardIds("serviceIds"));

            var result = await sender.Send(new GetOperationsDashboardQuery(
                fromUtc,
                toUtc,
                parsedStationIds,
                parsedCustomerIds,
                parsedServiceIds,
                topCount,
                includeAnalytics,
                includeOptions), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Dashboard.ViewAnalytics).WithTags("Operations.Dashboard");

        group.MapGet("/analytics-dashboard/flights", async (
            ISender sender,
            CancellationToken ct,
            int page = 1,
            int pageSize = 20,
            DateTimeOffset? fromUtc = null,
            DateTimeOffset? toUtc = null,
            string? stationIds = null,
            string? customerIds = null,
            string? serviceIds = null,
            string? sort = null) =>
        {
            if (!TryParseDashboardIds(stationIds, out var parsedStationIds))
                return ApiResults.Problem(InvalidDashboardIds("stationIds"));
            if (!TryParseDashboardIds(customerIds, out var parsedCustomerIds))
                return ApiResults.Problem(InvalidDashboardIds("customerIds"));
            if (!TryParseDashboardIds(serviceIds, out var parsedServiceIds))
                return ApiResults.Problem(InvalidDashboardIds("serviceIds"));

            var result = await sender.Send(new GetDashboardFlightsQuery(
                page,
                pageSize,
                fromUtc,
                toUtc,
                parsedStationIds,
                parsedCustomerIds,
                parsedServiceIds,
                sort), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Dashboard.ViewAnalytics)
            .WithTags("Operations.Dashboard")
            .WithName("GetDashboardFlights");

        group.MapGet("/analytics-dashboard/flights/export", async (
            string format,
            ISender sender,
            TimeProvider timeProvider,
            CancellationToken ct,
            DateTimeOffset? fromUtc = null,
            DateTimeOffset? toUtc = null,
            string? stationIds = null,
            string? customerIds = null,
            string? serviceIds = null,
            string? sort = null) =>
        {
            if (!DashboardFlightExportDocumentFactory.TryParseFormat(format, out var exportFormat))
            {
                return ApiResults.Problem(Error.Validation(
                    new Dictionary<string, string[]>
                    {
                        ["format"] = ["Format must be one of: xlsx, csv, or pdf."]
                    },
                    code: "Operations.Dashboard.ExportFormatInvalid"));
            }

            if (!TryParseDashboardIds(stationIds, out var parsedStationIds))
                return ApiResults.Problem(InvalidDashboardIds("stationIds"));
            if (!TryParseDashboardIds(customerIds, out var parsedCustomerIds))
                return ApiResults.Problem(InvalidDashboardIds("customerIds"));
            if (!TryParseDashboardIds(serviceIds, out var parsedServiceIds))
                return ApiResults.Problem(InvalidDashboardIds("serviceIds"));

            var result = await sender.Send(new GetDashboardFlightsExportQuery(
                fromUtc,
                toUtc,
                parsedStationIds,
                parsedCustomerIds,
                parsedServiceIds,
                sort), ct);
            if (result.IsFailure)
                return ApiResults.Problem(result.Error);

            var file = DashboardFlightExportDocumentFactory.Create(
                exportFormat,
                result.Value,
                new DashboardFlightExportCriteria(
                    fromUtc,
                    toUtc,
                    parsedStationIds,
                    parsedCustomerIds,
                    parsedServiceIds),
                timeProvider.GetUtcNow());
            return Results.File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: false);
        }).RequirePermission(OperationsPermissions.Dashboard.ViewAnalytics)
            .RequirePermission(OperationsPermissions.Dashboard.Export)
            .WithTags("Operations.Dashboard")
            .WithName("ExportDashboardFlights");
    }

    private static IReadOnlyList<FlightStatus>? ParseStatuses(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var statuses = new List<FlightStatus>();
        foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<FlightStatus>(item, ignoreCase: true, out var status))
                return null;
            if (!statuses.Contains(status))
                statuses.Add(status);
        }
        return statuses;
    }

    private static IReadOnlyList<FlightServiceCategory>? ParseServiceCategories(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var categories = new List<FlightServiceCategory>();
        foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<FlightServiceCategory>(item, ignoreCase: true, out var category))
                return null;
            if (!categories.Contains(category))
                categories.Add(category);
        }
        return categories;
    }

    private static bool TryParseDashboardIds(string? value, out IReadOnlyList<Guid> ids)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ids = [];
            return true;
        }

        var parsed = new List<Guid>();
        foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(item, out var id) || id == Guid.Empty)
            {
                ids = [];
                return false;
            }

            if (!parsed.Contains(id))
                parsed.Add(id);
        }

        if (parsed.Count == 0)
        {
            ids = [];
            return false;
        }

        ids = parsed;
        return true;
    }

    private static Error InvalidDashboardIds(string field) =>
        Error.Validation(
            new Dictionary<string, string[]>
            {
                [field] = [$"{field} must be a comma-separated list of non-empty GUID values."]
            },
            code: "Operations.Dashboard.FilterIdsInvalid");
}
