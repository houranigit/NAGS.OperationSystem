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

internal static class AtaChapterCategoryEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        var items = group.MapGroup("/ata-chapter-categories").WithTags("MasterData.AtaChapterCategories");

        items.MapGet("/", async (ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? search = null, bool? isActive = null, string? sort = null) =>
        {
            var result = await sender.Send(new GetAtaChapterCategoriesQuery(page, pageSize, search, isActive, sort), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.AtaChapterCategories.View);

        items.MapGet("/options", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetActiveAtaChapterCategoryOptionsQuery(), ct);
            return result.ToOk();
        }).RequireAnyPermission(MasterDataPermissions.Reference.ViewOptions, MasterDataPermissions.AtaChapterCategories.View);

        items.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetAtaChapterCategoryByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.AtaChapterCategories.View);

        items.MapPost("/", async (CreateAtaChapterCategoryRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateAtaChapterCategoryCommand(request.Name), ct);
            return result.ToCreated(id => $"/api/v1/masterdata/ata-chapter-categories/{id}");
        }).RequirePermission(MasterDataPermissions.AtaChapterCategories.Create);

        items.MapPut("/{id:guid}", async (Guid id, UpdateAtaChapterCategoryRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new UpdateAtaChapterCategoryCommand(id, request.Name, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.AtaChapterCategories.Update);

        items.MapPost("/{id:guid}/activate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new ActivateAtaChapterCategoryCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.AtaChapterCategories.Activate);

        items.MapPost("/{id:guid}/deactivate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new DeactivateAtaChapterCategoryCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.AtaChapterCategories.Deactivate);
    }
}
