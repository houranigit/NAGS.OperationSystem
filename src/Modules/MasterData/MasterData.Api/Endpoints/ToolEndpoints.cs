using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Concurrency;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Application.Persistence;
using MasterData.Application.Features.Tools;
using MasterData.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MasterData.Api.Endpoints;

internal static class ToolEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        var tools = group.MapGroup("/tools").WithTags("MasterData.Tools");

        tools.MapGet("/", async (ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? search = null, bool? isActive = null, string? sort = null) =>
        {
            var result = await sender.Send(new GetToolsQuery(page, pageSize, search, isActive, sort), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.Tools.View);

        tools.MapGet("/options", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetActiveToolOptionsQuery(), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.Reference.ViewOptions);

        tools.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetToolByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.Tools.View);

        tools.MapPost("/", async (CreateToolRequest request, ISender sender, CancellationToken ct) =>
        {
            var equipment = request.Equipments?.Select(e => new ToolEquipmentInput(e.Id, e.FactoryId, e.SerialId, e.CalibrationDate)).ToList();
            var result = await sender.Send(new CreateToolCommand(request.Name, request.Description, equipment), ct);
            return result.ToCreated(id => $"/api/v1/masterdata/tools/{id}");
        }).RequirePermission(MasterDataPermissions.Tools.Create);

        tools.MapPut("/{id:guid}", async (Guid id, UpdateToolRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var equipment = request.Equipments?.Select(e => new ToolEquipmentInput(e.Id, e.FactoryId, e.SerialId, e.CalibrationDate)).ToList();
            var result = await sender.Send(new UpdateToolCommand(id, request.Name, request.Description, equipment, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.Tools.Update);

        tools.MapPost("/{id:guid}/activate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new ActivateToolCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.Tools.Activate);

        tools.MapPost("/{id:guid}/deactivate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new DeactivateToolCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.Tools.Deactivate);
    }
}
