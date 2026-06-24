using Audit.Api.Endpoints;
using BuildingBlocks.Api.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Audit.Api;

/// <summary>Maps all Audit endpoints under <c>/api/v1/audit</c>.</summary>
public sealed class AuditEndpointModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/audit");
        AuditTrailEndpoints.Map(group);
    }
}
