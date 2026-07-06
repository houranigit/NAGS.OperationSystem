using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Concurrency;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Application.Persistence;
using MasterData.Application.Features.AircraftTypes;
using MasterData.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MasterData.Api.Endpoints;

internal static class AircraftTypeEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        var aircraftTypes = group.MapGroup("/aircraft-types").WithTags("MasterData.AircraftTypes");

        aircraftTypes.MapGet("/", async (ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? search = null, bool? isActive = null, string? sort = null) =>
        {
            var result = await sender.Send(new GetAircraftTypesQuery(page, pageSize, search, isActive, sort), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.AircraftTypes.View);

        aircraftTypes.MapGet("/options", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetActiveAircraftTypeOptionsQuery(), ct);
            return result.ToOk();
        }).RequireAnyPermission(MasterDataPermissions.Reference.ViewOptions, MasterDataPermissions.AircraftTypes.View);

        aircraftTypes.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetAircraftTypeByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.AircraftTypes.View);

        aircraftTypes.MapPost("/", async (CreateAircraftTypeRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateAircraftTypeCommand(request.Manufacturer, request.Model, request.Notes), ct);
            return result.ToCreated(id => $"/api/v1/masterdata/aircraft-types/{id}");
        }).RequirePermission(MasterDataPermissions.AircraftTypes.Create);

        aircraftTypes.MapPut("/{id:guid}", async (Guid id, UpdateAircraftTypeRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new UpdateAircraftTypeCommand(id, request.Manufacturer, request.Model, request.Notes, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.AircraftTypes.Update);

        aircraftTypes.MapPost("/{id:guid}/activate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new ActivateAircraftTypeCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.AircraftTypes.Activate);

        aircraftTypes.MapPost("/{id:guid}/deactivate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new DeactivateAircraftTypeCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.AircraftTypes.Deactivate);
    }
}
