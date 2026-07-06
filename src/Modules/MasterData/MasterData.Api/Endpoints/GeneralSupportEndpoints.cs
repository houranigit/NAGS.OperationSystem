using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Concurrency;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Application.Persistence;
using MasterData.Application.Features.GeneralSupports;
using MasterData.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MasterData.Api.Endpoints;

internal static class GeneralSupportEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        var supports = group.MapGroup("/general-supports").WithTags("MasterData.GeneralSupports");

        supports.MapGet("/", async (ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? search = null, bool? isActive = null, string? sort = null) =>
        {
            var result = await sender.Send(new GetGeneralSupportsQuery(page, pageSize, search, isActive, sort), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.GeneralSupports.View);

        supports.MapGet("/options", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetActiveGeneralSupportOptionsQuery(), ct);
            return result.ToOk();
        }).RequireAnyPermission(MasterDataPermissions.Reference.ViewOptions, MasterDataPermissions.GeneralSupports.View);

        supports.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetGeneralSupportByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.GeneralSupports.View);

        supports.MapPost("/", async (CreateGeneralSupportRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateGeneralSupportCommand(request.Name, request.Description), ct);
            return result.ToCreated(id => $"/api/v1/masterdata/general-supports/{id}");
        }).RequirePermission(MasterDataPermissions.GeneralSupports.Create);

        supports.MapPut("/{id:guid}", async (Guid id, UpdateGeneralSupportRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new UpdateGeneralSupportCommand(id, request.Name, request.Description, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.GeneralSupports.Update);

        supports.MapPost("/{id:guid}/activate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new ActivateGeneralSupportCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.GeneralSupports.Activate);

        supports.MapPost("/{id:guid}/deactivate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new DeactivateGeneralSupportCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.GeneralSupports.Deactivate);
    }
}
