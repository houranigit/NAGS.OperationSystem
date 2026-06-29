using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Concurrency;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Application.Persistence;
using MasterData.Application.Features.Services;
using MasterData.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MasterData.Api.Endpoints;

internal static class ServiceEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        var services = group.MapGroup("/services").WithTags("MasterData.Services");

        services.MapGet("/", async (ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? search = null, bool? isActive = null, string? sort = null) =>
        {
            var result = await sender.Send(new GetServicesQuery(page, pageSize, search, isActive, sort), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.Services.View);

        services.MapGet("/options", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetActiveServiceOptionsQuery(), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.Reference.ViewOptions);

        services.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetServiceByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.Services.View);

        services.MapPost("/", async (CreateServiceRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateServiceCommand(request.Name, request.Description), ct);
            return result.ToCreated(id => $"/api/v1/masterdata/services/{id}");
        }).RequirePermission(MasterDataPermissions.Services.Create);

        services.MapPut("/{id:guid}", async (Guid id, UpdateServiceRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new UpdateServiceCommand(id, request.Name, request.Description, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.Services.Update);

        services.MapPost("/{id:guid}/activate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new ActivateServiceCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.Services.Activate);

        services.MapPost("/{id:guid}/deactivate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new DeactivateServiceCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.Services.Deactivate);
    }
}
