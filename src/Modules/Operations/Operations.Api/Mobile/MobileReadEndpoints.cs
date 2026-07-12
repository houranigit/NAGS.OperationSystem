using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Operations.Application.Features.Mobile;
using Operations.Application.Features.WorkOrders;
using Operations.Domain.Authorization;

namespace Operations.Api.Mobile;

/// <summary>
/// Read surface of the dedicated mobile BFF. The mobile client fills its offline cache from these
/// endpoints: full-table refreshes on sync, and the single-flight fetch as the realtime upsert
/// apply path. All responses are shaped for the client's local database, not for the web portal.
/// </summary>
internal static class MobileReadEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        // Profile + catalogs + roster: any authenticated staff account; the handlers fail closed
        // unless the caller resolves to an active StaffMember at an active Station.
        group.MapGet("/me", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetMobileMeQuery(), ct);
            return result.ToOk();
        }).RequireAuthorization();

        group.MapGet("/catalogs", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetMobileCatalogsQuery(), ct);
            return result.ToOk();
        }).RequireAuthorization();

        group.MapGet("/employees/at-my-station", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetMobileStationStaffQuery(), ct);
            return result.ToOk();
        }).RequireAuthorization();

        // Flight lists feed the client's three cache tables. windowHours clamps to 1-168 (default 12).
        group.MapGet("/flights/my", async (ISender sender, CancellationToken ct, int windowHours = 12) =>
        {
            var result = await sender.Send(new GetMobileFlightsQuery(MobileFlightList.My, windowHours), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.View);

        group.MapGet("/flights/per-landing", async (ISender sender, CancellationToken ct, int windowHours = 12) =>
        {
            var result = await sender.Send(new GetMobileFlightsQuery(MobileFlightList.PerLanding, windowHours), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.View);

        group.MapGet("/flights/ad-hoc", async (ISender sender, CancellationToken ct, int windowHours = 12) =>
        {
            var result = await sender.Send(new GetMobileFlightsQuery(MobileFlightList.AdHoc, windowHours), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.View);

        group.MapGet("/flights/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetMobileFlightByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.Flights.View);

        group.MapGet("/flights/{flightId:guid}/work-orders/mine", async (Guid flightId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetMobileMyWorkOrderForFlightQuery(flightId), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.WorkOrders.View);

        group.MapGet("/work-orders/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetWorkOrderByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(OperationsPermissions.WorkOrders.View);
    }
}
