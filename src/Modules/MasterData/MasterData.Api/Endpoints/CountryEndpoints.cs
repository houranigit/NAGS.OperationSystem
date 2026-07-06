using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Concurrency;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Application.Persistence;
using MasterData.Application.Features.Countries;
using MasterData.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MasterData.Api.Endpoints;

internal static class CountryEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        var countries = group.MapGroup("/countries").WithTags("MasterData.Countries");

        countries.MapGet("/", async (ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? search = null, bool? isActive = null, string? sort = null) =>
        {
            var result = await sender.Send(new GetCountriesQuery(page, pageSize, search, isActive, sort), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.Countries.View);

        countries.MapGet("/options", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetActiveCountryOptionsQuery(), ct);
            return result.ToOk();
        }).RequireAnyPermission(MasterDataPermissions.Reference.ViewOptions, MasterDataPermissions.Countries.View);

        countries.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetCountryByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.Countries.View);

        countries.MapPost("/", async (CreateCountryRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateCountryCommand(request.Name, request.IsoCode), ct);
            return result.ToCreated(id => $"/api/v1/masterdata/countries/{id}");
        }).RequirePermission(MasterDataPermissions.Countries.Create);

        countries.MapPut("/{id:guid}", async (Guid id, UpdateCountryRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new UpdateCountryCommand(id, request.Name, request.IsoCode, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.Countries.Update);

        countries.MapPost("/{id:guid}/activate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new ActivateCountryCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.Countries.Activate);

        countries.MapPost("/{id:guid}/deactivate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new DeactivateCountryCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.Countries.Deactivate);
    }
}
