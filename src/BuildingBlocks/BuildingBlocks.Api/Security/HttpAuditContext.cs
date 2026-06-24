using System.Diagnostics;
using System.Security.Claims;
using BuildingBlocks.Application.Auditing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Api.Security;

/// <summary>
/// Resolves the acting principal and correlation id for audit capture from the current request.
/// When there is no authenticated user (background jobs, seeding) it reports a system actor.
/// </summary>
public sealed class HttpAuditContext(IHttpContextAccessor accessor) : IAuditContext
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    private bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid? ActorId
    {
        get
        {
            var value = Principal?.FindFirstValue("sub") ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public string? ActorDisplayName =>
        Principal?.FindFirstValue("name") ?? Principal?.FindFirstValue(ClaimTypes.Name);

    public bool IsSystemActor => !IsAuthenticated;

    public string? CorrelationId =>
        accessor.HttpContext?.TraceIdentifier ?? Activity.Current?.Id;
}

public static class AuditContextExtensions
{
    public static IServiceCollection AddHttpAuditContext(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IAuditContext, HttpAuditContext>();
        return services;
    }
}
