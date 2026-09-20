using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Concurrency;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Application.Persistence;
using MasterData.Application.Features.AtaChapters;
using MasterData.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MasterData.Api.Endpoints;

internal static class AtaChapterEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        var items = group.MapGroup("/ata-chapters").WithTags("MasterData.AtaChapters");

        items.MapGet("/", async (ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? search = null, bool? isActive = null, string? sort = null, Guid? categoryId = null, bool? effectiveActive = null) =>
        {
            var result = await sender.Send(new GetAtaChaptersQuery(page, pageSize, search, isActive, sort, categoryId, effectiveActive), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.AtaChapters.View);

        items.MapGet("/options", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetActiveAtaChapterOptionsQuery(), ct);
            return result.ToOk();
        }).RequireAnyPermission(MasterDataPermissions.Reference.ViewOptions, MasterDataPermissions.AtaChapters.View);

        items.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetAtaChapterByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.AtaChapters.View);

        items.MapPost("/", async (CreateAtaChapterRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateAtaChapterCommand(request.CategoryId, request.Code, request.Title), ct);
            return result.ToCreated(id => $"/api/v1/masterdata/ata-chapters/{id}");
        }).RequirePermission(MasterDataPermissions.AtaChapters.Create);

        items.MapPut("/{id:guid}", async (Guid id, UpdateAtaChapterRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new UpdateAtaChapterCommand(id, request.CategoryId, request.Code, request.Title, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.AtaChapters.Update);

        items.MapPost("/{id:guid}/activate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new ActivateAtaChapterCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.AtaChapters.Activate);

        items.MapPost("/{id:guid}/deactivate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new DeactivateAtaChapterCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.AtaChapters.Deactivate);
    }
}
