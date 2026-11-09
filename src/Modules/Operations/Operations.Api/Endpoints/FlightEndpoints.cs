using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Concurrency;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Application.Persistence;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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

        flights.MapPut("/{id:guid}/arrival", async (Guid id, SaveFlightArrivalRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);
            var result = await sender.Send(new SaveFlightArrivalCommand(id, request, rowVersion), ct);
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
