using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Concurrency;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Application.Persistence;
using MasterData.Application.Features.Licenses;
using MasterData.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MasterData.Api.Endpoints;

internal static class LicenseEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        var licenses = group.MapGroup("/licenses").WithTags("MasterData.Licenses");

        licenses.MapGet("/", async (ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? search = null, bool? isActive = null, string? sort = null) =>
        {
            var result = await sender.Send(new GetLicensesQuery(page, pageSize, search, isActive, sort), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.Licenses.View);

        licenses.MapGet("/options", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetActiveLicenseOptionsQuery(), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.Licenses.View);

        licenses.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetLicenseByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.Licenses.View);

        licenses.MapPost("/", async (CreateLicenseRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateLicenseCommand(request.Code, request.Name, request.Description), ct);
            return result.ToCreated(id => $"/api/v1/masterdata/licenses/{id}");
        }).RequirePermission(MasterDataPermissions.Licenses.Create);

        licenses.MapPut("/{id:guid}", async (Guid id, UpdateLicenseRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new UpdateLicenseCommand(id, request.Name, request.Description, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.Licenses.Update);

        licenses.MapPost("/{id:guid}/activate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new ActivateLicenseCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.Licenses.Activate);

        licenses.MapPost("/{id:guid}/deactivate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new DeactivateLicenseCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.Licenses.Deactivate);
    }
}
