using Microsoft.AspNetCore.Routing;

namespace BuildingBlocks.Api.Modules;

/// <summary>
/// Implemented by each module's API project to map its own endpoints. The host composes
/// modules by discovering and invoking these; the host itself contains no business endpoints.
/// </summary>
public interface IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app);
}
