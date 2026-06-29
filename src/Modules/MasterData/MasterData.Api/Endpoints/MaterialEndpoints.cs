using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Concurrency;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Application.Persistence;
using MasterData.Application.Features.Materials;
using MasterData.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MasterData.Api.Endpoints;

internal static class MaterialEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        var materials = group.MapGroup("/materials").WithTags("MasterData.Materials");

        materials.MapGet("/", async (ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? search = null, bool? isActive = null, string? sort = null) =>
        {
            var result = await sender.Send(new GetMaterialsQuery(page, pageSize, search, isActive, sort), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.Materials.View);

        materials.MapGet("/options", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetActiveMaterialOptionsQuery(), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.Reference.ViewOptions);

        materials.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetMaterialByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.Materials.View);

        materials.MapPost("/", async (CreateMaterialRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateMaterialCommand(request.Name, request.Description), ct);
            return result.ToCreated(id => $"/api/v1/masterdata/materials/{id}");
        }).RequirePermission(MasterDataPermissions.Materials.Create);

        materials.MapPut("/{id:guid}", async (Guid id, UpdateMaterialRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new UpdateMaterialCommand(id, request.Name, request.Description, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.Materials.Update);

        materials.MapPost("/{id:guid}/activate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new ActivateMaterialCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.Materials.Activate);

        materials.MapPost("/{id:guid}/deactivate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new DeactivateMaterialCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.Materials.Deactivate);
    }
}
