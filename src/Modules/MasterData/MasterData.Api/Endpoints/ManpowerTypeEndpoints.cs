using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Concurrency;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Application.Persistence;
using MasterData.Application.Features.ManpowerTypes;
using MasterData.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MasterData.Api.Endpoints;

internal static class ManpowerTypeEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        var manpowerTypes = group.MapGroup("/manpower-types").WithTags("MasterData.ManpowerTypes");

        manpowerTypes.MapGet("/", async (ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? search = null, bool? isActive = null, string? sort = null) =>
        {
            var result = await sender.Send(new GetManpowerTypesQuery(page, pageSize, search, isActive, sort), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.ManpowerTypes.View);

        manpowerTypes.MapGet("/options", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetActiveManpowerTypeOptionsQuery(), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.ManpowerTypes.View);

        manpowerTypes.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetManpowerTypeByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.ManpowerTypes.View);

        manpowerTypes.MapPost("/", async (CreateManpowerTypeRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateManpowerTypeCommand(request.Name, request.Description), ct);
            return result.ToCreated(id => $"/api/v1/masterdata/manpower-types/{id}");
        }).RequirePermission(MasterDataPermissions.ManpowerTypes.Create);

        manpowerTypes.MapPut("/{id:guid}", async (Guid id, UpdateManpowerTypeRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new UpdateManpowerTypeCommand(id, request.Name, request.Description, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.ManpowerTypes.Update);

        manpowerTypes.MapPost("/{id:guid}/activate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new ActivateManpowerTypeCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.ManpowerTypes.Activate);

        manpowerTypes.MapPost("/{id:guid}/deactivate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new DeactivateManpowerTypeCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.ManpowerTypes.Deactivate);
    }
}
