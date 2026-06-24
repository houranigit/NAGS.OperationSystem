using Audit.Application.Abstractions;
using Audit.Application.Authorization;
using Audit.Application.Features.Trails;
using Audit.Infrastructure.Persistence;
using BuildingBlocks.Contracts.Auditing;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Audit.Infrastructure;

public static class AuditInfrastructureExtensions
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Audit")
            ?? configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("No 'Audit' or 'Default' connection string configured.");

        services.AddDbContext<AuditDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", AuditDbContext.Schema)));

        services.AddScoped<IAuditDbContext>(sp => sp.GetRequiredService<AuditDbContext>());

        services.TryAddSingleton(TimeProvider.System);

        // Contribute Audit's permissions to the composed cross-module registry.
        services.AddSingleton<IPermissionCatalog, AuditPermissionCatalog>();

        // Consume the cross-cutting audit event produced by every module's outbox.
        services.AddModuleOutbox<AuditDbContext>();
        services.AddIntegrationEventHandler<AuditEntryRecorded, AuditEntryRecordedHandler>();

        return services;
    }

    public static async Task MigrateAuditAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
