using BuildingBlocks.Api.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Operations.Api.Endpoints;
using Operations.Api.Exports;
using Operations.Application.Features.Flights;

namespace Operations.Api;

public static class OperationsApiExtensions
{
    public static IServiceCollection AddOperationsApi(this IServiceCollection services) =>
        services.AddSingleton<IFlightWorkOrderPdfRenderer, FlightWorkOrderPdfRenderer>();
}

/// <summary>
/// Maps flight scheduling endpoints under <c>/api/v1/operations</c>.
/// </summary>
public sealed class OperationsEndpointModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/operations");

        FlightEndpoints.Map(group);
    }
}
