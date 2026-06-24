using BuildingBlocks.Application.Auditing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Infrastructure.Auditing;

public static class AuditCaptureExtensions
{
    /// <summary>
    /// Registers the automatic-capture interceptor and a system-actor fallback audit context. Each
    /// module DbContext adds the interceptor (when present) so audited changes are written to that
    /// context's outbox in the same transaction. The host overrides the fallback context with a
    /// claims-backed implementation.
    /// </summary>
    public static IServiceCollection AddAuditCapture(this IServiceCollection services)
    {
        services.TryAddScoped<IAuditContext, SystemAuditContext>();
        services.AddScoped<AuditSaveChangesInterceptor>();
        return services;
    }
}
