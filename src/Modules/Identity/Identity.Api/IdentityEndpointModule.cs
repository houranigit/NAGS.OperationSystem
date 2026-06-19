using BuildingBlocks.Api.Modules;
using Identity.Api.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Identity.Api;

/// <summary>Maps all Identity endpoints under <c>/api/v1/identity</c>.</summary>
public sealed class IdentityEndpointModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/identity");

        AuthEndpoints.Map(group);
        RoleEndpoints.Map(group);
        UserEndpoints.Map(group);
        SessionEndpoints.Map(group);
    }
}
