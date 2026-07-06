using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Concurrency;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Application.Persistence;
using MasterData.Application.Features.OperationTypes;
using MasterData.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MasterData.Api.Endpoints;

internal static class OperationTypeEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        var operationTypes = group.MapGroup("/operation-types").WithTags("MasterData.OperationTypes");

        operationTypes.MapGet("/", async (ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? search = null, bool? isActive = null, string? sort = null) =>
        {
            var result = await sender.Send(new GetOperationTypesQuery(page, pageSize, search, isActive, sort), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.OperationTypes.View);

        operationTypes.MapGet("/options", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetActiveOperationTypeOptionsQuery(), ct);
            return result.ToOk();
        }).RequireAnyPermission(MasterDataPermissions.Reference.ViewOptions, MasterDataPermissions.OperationTypes.View);

        operationTypes.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetOperationTypeByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.OperationTypes.View);

        operationTypes.MapPost("/", async (CreateOperationTypeRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateOperationTypeCommand(request.Name, request.Description), ct);
            return result.ToCreated(id => $"/api/v1/masterdata/operation-types/{id}");
        }).RequirePermission(MasterDataPermissions.OperationTypes.Create);

        operationTypes.MapPut("/{id:guid}", async (Guid id, UpdateOperationTypeRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new UpdateOperationTypeCommand(id, request.Name, request.Description, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.OperationTypes.Update);

        operationTypes.MapPost("/{id:guid}/activate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new ActivateOperationTypeCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.OperationTypes.Activate);

        operationTypes.MapPost("/{id:guid}/deactivate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new DeactivateOperationTypeCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.OperationTypes.Deactivate);
    }
}
