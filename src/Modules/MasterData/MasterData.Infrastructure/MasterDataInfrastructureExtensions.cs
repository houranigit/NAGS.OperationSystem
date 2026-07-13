using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Infrastructure.Auditing;
using BuildingBlocks.Infrastructure.Messaging;
using Identity.Contracts;
using MasterData.Application.Abstractions;
using MasterData.Application.Authorization;
using MasterData.Application.Features.PortalAccess;
using MasterData.Contracts.Readers;
using MasterData.Infrastructure.Persistence;
using MasterData.Infrastructure.Readers;
using MasterData.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MasterData.Infrastructure;

public static class MasterDataInfrastructureExtensions
{
    public static IServiceCollection AddMasterDataModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MasterData")
            ?? configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("No 'MasterData' or 'Default' connection string configured.");

        services.AddDbContext<MasterDataDbContext>((sp, options) =>
        {
            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", MasterDataDbContext.Schema));

            if (sp.GetService<AuditSaveChangesInterceptor>() is { } auditInterceptor)
                options.AddInterceptors(auditInterceptor);
        });

        services.AddScoped<IMasterDataDbContext>(sp => sp.GetRequiredService<MasterDataDbContext>());

        services.AddScoped<IMasterDataScope, MasterDataScope>();

        // Cross-module read seam consumed by Operations (validation + snapshotting).
        services.AddScoped<IMasterDataReader, MasterDataReader>();
        services.AddScoped<IStaffNotificationReader, StaffNotificationReader>();

        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IPermissionCatalog, MasterDataPermissionCatalog>();
        services.AddModuleOutbox<MasterDataDbContext>();
        services.AddIntegrationEventHandler<PortalUserProvisioned, PortalUserProvisionedHandler>();
        services.AddIntegrationEventHandler<PortalUserProvisioningFailed, PortalUserProvisioningFailedHandler>();
        services.AddIntegrationEventHandler<PortalUserActivated, PortalUserActivatedHandler>();
        services.AddIntegrationEventHandler<PortalUserAccessRestored, PortalUserAccessRestoredHandler>();
        services.AddIntegrationEventHandler<PortalUserEmailChangeConfirmed, PortalUserEmailChangeConfirmedHandler>();
        services.AddIntegrationEventHandler<PortalUserEmailChangeFailed, PortalUserEmailChangeFailedHandler>();
        services.AddIntegrationEventHandler<PortalUserDeactivated, PortalUserDeactivatedHandler>();

        services.AddScoped<MasterDataDataSeeder>();

        return services;
    }

    public static async Task MigrateAndSeedMasterDataAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MasterDataDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        var seeder = scope.ServiceProvider.GetRequiredService<MasterDataDataSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}
