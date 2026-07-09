using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Concurrency;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Application.Persistence;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Operations.Application.Features.Dashboard;
using Operations.Application.Features.Flights;
using Operations.Application.Features.Merge;
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
            Guid? operationTypeId = null, FlightStatus? status = null, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, string? sort = null) =>
        {
            var result = await sender.Send(new GetFlightsQuery(page, pageSize, search, stationId, customerId, operationTypeId, status, fromUtc, toUtc, sort), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.View);

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

        group.MapGet("/dashboard", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetOperationsDashboardQuery(), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Dashboard.View).WithTags("Operations.Dashboard");
    }
}
