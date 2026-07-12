using BuildingBlocks.Api.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Operations.Api.Endpoints;
using Operations.Api.Mobile;

namespace Operations.Api;

/// <summary>
/// Maps all Operations endpoints under <c>/api/v1/operations</c>, plus the dedicated mobile
/// surface (BFF reads, offline-sync catch-up, and idempotent writes) under <c>/api/v1/mobile</c>.
/// </summary>
public sealed class OperationsEndpointModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/operations");

        FlightEndpoints.Map(group);
        WorkOrderEndpoints.Map(group);

        var mobile = app.MapGroup("/api/v1/mobile").WithTags("Operations.Mobile");
        MobileReadEndpoints.Map(mobile);
        MobileSyncEndpoints.Map(mobile);
        MobileWriteEndpoints.Map(mobile);
    }
}
