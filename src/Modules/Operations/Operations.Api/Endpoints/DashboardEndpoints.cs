using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Operations.Api.Exports;
using Operations.Application.Features.Dashboard;
using Operations.Application.Features.Flights;
using Operations.Domain.Authorization;

namespace Operations.Api.Endpoints;

internal static class DashboardEndpoints
{
    internal static void Map(RouteGroupBuilder operations)
    {
        var dashboard = operations.MapGroup("/analytics-dashboard")
            .WithTags("Operations.Dashboard")
            .RequirePermission(OperationsPermissions.Dashboard.ViewAnalytics);

        dashboard.MapGet("", GetDashboardAsync);
        dashboard.MapGet("/flights", GetFlightsAsync).WithName("GetDashboardFlights");
        dashboard.MapGet("/flights/export", ExportFlightsAsync)
            .RequirePermission(OperationsPermissions.Dashboard.Export).WithName("ExportDashboardFlights");
    }

    private static async Task<IResult> GetDashboardAsync(
        ISender sender,
        CancellationToken ct,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? stationIds = null,
        string? customerIds = null,
        string? serviceIds = null,
        int topCount = 5,
        bool includeOptions = true,
        string? timeZoneId = null)
    {
        var filters = ParseFilters(fromUtc, toUtc, stationIds, customerIds, serviceIds);
        if (filters.IsFailure)
            return ApiResults.Problem(filters.Error);
        if (!TryResolveZone(timeZoneId, out var zone))
            return ApiResults.Problem(InvalidTimeZone());

        return (await sender.Send(new GetOperationsDashboardQuery(filters.Value, topCount, includeOptions, zone), ct)).ToOk();
    }

    private static async Task<IResult> GetFlightsAsync(
        ISender sender,
        CancellationToken ct,
        int page = 1,
        int pageSize = 20,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? stationIds = null,
        string? customerIds = null,
        string? serviceIds = null,
        string? sort = null)
    {
        var filters = ParseFilters(fromUtc, toUtc, stationIds, customerIds, serviceIds);
        return filters.IsFailure
            ? ApiResults.Problem(filters.Error)
            : (await sender.Send(new GetDashboardFlightsQuery(filters.Value, page, pageSize, sort), ct)).ToOk();
    }

    private static async Task<IResult> ExportFlightsAsync(
        string format,
        HttpResponse response,
        ISender sender,
        ILegacySystemIdReader legacySystemIds,
        TimeProvider clock,
        CancellationToken ct,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? stationIds = null,
        string? customerIds = null,
        string? serviceIds = null,
        string? sort = null,
        string? timeZoneId = null)
    {
        if (!FlightExportDocumentFactory.TryParseFormat(format, out var exportFormat))
            return ApiResults.Problem(Error.Validation(
                "Format must be one of: xlsx, csv, or pdf.", "Operations.Dashboard.ExportFormatInvalid"));
        if (!TryResolveZone(timeZoneId, out var zone))
            return ApiResults.Problem(InvalidTimeZone());

        var filters = ParseFilters(fromUtc, toUtc, stationIds, customerIds, serviceIds);
        if (filters.IsFailure)
            return ApiResults.Problem(filters.Error);

        var filter = filters.Value;
        var result = await sender.Send(new GetDashboardFlightsExportQuery(filter, sort), ct);
        if (result.IsFailure)
            return ApiResults.Problem(result.Error);

        var rows = result.Value;
        var legacyIds = exportFormat == FlightExportFormat.Xlsx
            ? await legacySystemIds.GetAsync(FlightExportDocumentFactory.CollectLegacySystemIdRequest(rows), ct)
            : null;
        // The common report shows an inclusive end; the dashboard filter uses an exclusive one.
        var reportThroughUtc = filter.ToUtc is { } exclusiveEnd && exclusiveEnd > DateTimeOffset.MinValue
            ? exclusiveEnd.AddTicks(-1) : filter.ToUtc;
        var criteria = new FlightExportCriteria(null, filter.StationIds, filter.CustomerIds, null, null,
            filter.FromUtc, reportThroughUtc, FlightListType.All, PerformedServiceIds: filter.ServiceIds);
        var file = FlightExportDocumentFactory.Create(exportFormat, rows, criteria, clock.GetUtcNow(), zone, legacyIds);
        response.Headers.CacheControl = "private, no-store";
        return Results.File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: false);
    }

    private static Result<DashboardFlightFilter> ParseFilters(
        DateTimeOffset? fromUtc, DateTimeOffset? toUtc, string? stationIds, string? customerIds, string? serviceIds)
    {
        if (!TryParseIds(stationIds, out var stations))
            return InvalidIds(nameof(stationIds));
        if (!TryParseIds(customerIds, out var customers))
            return InvalidIds(nameof(customerIds));
        if (!TryParseIds(serviceIds, out var services))
            return InvalidIds(nameof(serviceIds));

        return new DashboardFlightFilter(fromUtc?.ToUniversalTime(), toUtc?.ToUniversalTime(), stations, customers, services);
    }

    private static bool TryParseIds(string? value, out IReadOnlyList<Guid> ids)
    {
        var parsedIds = new HashSet<Guid>();
        ids = [];
        if (string.IsNullOrWhiteSpace(value))
            return true;

        foreach (var part in value.Split(',', StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(part, out var id) || id == Guid.Empty)
                return false;
            parsedIds.Add(id);
        }

        ids = parsedIds.ToArray();
        return true;
    }

    private static bool TryResolveZone(string? timeZoneId, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        return string.IsNullOrWhiteSpace(timeZoneId) || FlightExportTimeZoneResolver.TryResolve(timeZoneId, out zone);
    }

    private static Error InvalidIds(string field) => Error.Validation(
        new Dictionary<string, string[]> { [field] = ["Use a comma-separated list of valid IDs."] },
        code: "Operations.Dashboard.FilterIdsInvalid");

    private static Error InvalidTimeZone() => Error.Validation(
        new Dictionary<string, string[]> { ["timeZoneId"] = ["Time zone must be a valid IANA, Windows, or Browser UTC±HH:mm identifier."] },
        code: "Operations.Dashboard.TimeZoneInvalid");
}
