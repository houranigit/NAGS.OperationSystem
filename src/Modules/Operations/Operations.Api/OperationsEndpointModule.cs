using BuildingBlocks.Api.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Operations.Api.Endpoints;

namespace Operations.Api;

/// <summary>Maps all Operations endpoints under <c>/api/v1/operations</c>.</summary>
public sealed class OperationsEndpointModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/operations");

        FlightEndpoints.Map(group);
        WorkOrderEndpoints.Map(group);
    }
}
