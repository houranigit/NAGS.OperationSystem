using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Concurrency;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Application.Persistence;
using MasterData.Application.Features.Stations;
using MasterData.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MasterData.Api.Endpoints;

internal static class StationEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        var stations = group.MapGroup("/stations").WithTags("MasterData.Stations");

        stations.MapGet("/", async (ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? search = null, bool? isActive = null, Guid? countryId = null, string? sort = null) =>
        {
            var result = await sender.Send(new GetStationsQuery(page, pageSize, search, isActive, countryId, sort), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.Stations.View);

        stations.MapGet("/options", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetActiveStationOptionsQuery(), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.Stations.View);

        stations.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetStationByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.Stations.View);

        stations.MapPost("/", async (CreateStationRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateStationCommand(request.IataCode, request.IcaoCode, request.Name, request.City, request.CountryId), ct);
            return result.ToCreated(id => $"/api/v1/masterdata/stations/{id}");
        }).RequirePermission(MasterDataPermissions.Stations.Create);

        stations.MapPut("/{id:guid}", async (Guid id, UpdateStationRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new UpdateStationCommand(id, request.IataCode, request.IcaoCode, request.Name, request.City, request.CountryId, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.Stations.Update);

        stations.MapPost("/{id:guid}/activate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new ActivateStationCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.Stations.Activate);

        stations.MapPost("/{id:guid}/deactivate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new DeactivateStationCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.Stations.Deactivate);
    }
}
