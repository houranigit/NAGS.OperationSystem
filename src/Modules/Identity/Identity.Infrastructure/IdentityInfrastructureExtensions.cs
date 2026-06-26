using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Infrastructure.Auditing;
using BuildingBlocks.Infrastructure.Email;
using BuildingBlocks.Infrastructure.Messaging;
using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Application.Features.PortalAccess;
using Identity.Infrastructure.Notifications;
using MasterData.Contracts;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Seeding;
using Identity.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure;

public static class IdentityInfrastructureExtensions
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdentityModuleOptions>()
            .Bind(configuration.GetSection(IdentityModuleOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<IdentityModuleOptions>, IdentityModuleOptionsValidator>();

        var connectionString = configuration.GetConnectionString("Identity")
            ?? configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("No 'Identity' or 'Default' connection string configured.");

        services.AddDbContext<IdentityDbContext>((sp, options) =>
        {
            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema));

            // Automatic audit capture writes change events to this module's outbox in the same
            // transaction. Optional so the module can be composed without the audit host wiring.
            if (sp.GetService<AuditSaveChangesInterceptor>() is { } auditInterceptor)
                options.AddInterceptors(auditInterceptor);
        });

        services.AddScoped<IIdentityDbContext>(sp => sp.GetRequiredService<IdentityDbContext>());

        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddHttpContextAccessor();

        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<IMfaService, MfaService>();
        services.AddSingleton<IMfaSecretProtector, DataProtectionMfaSecretProtector>();
        services.AddScoped<ITokenSecurityValidator, TokenSecurityValidator>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IInvitationNotifier, EmailInvitationNotifier>();
        services.AddScoped<IPasswordResetNotifier, EmailPasswordResetNotifier>();
        services.AddScoped<ILinkedEmailVerificationNotifier, EmailLinkedEmailVerificationNotifier>();

        // Contribute Identity's permissions to the composed cross-module registry.
        services.AddSingleton<IPermissionCatalog, IdentityPermissionCatalog>();

        // Shared cross-cutting infrastructure used by Identity.
        services.AddEmailSender(configuration);
        services.AddModuleOutbox<IdentityDbContext>();
        services.AddIntegrationEventHandler<PortalAccessRequested, PortalAccessRequestedHandler>();
        services.AddIntegrationEventHandler<LinkedRecordDeactivated, LinkedRecordDeactivatedHandler>();
        services.AddIntegrationEventHandler<LinkedEmailChangeRequested, LinkedEmailChangeRequestedHandler>();

        services.AddScoped<IdentityDataSeeder>();

        return services;
    }

    public static async Task MigrateAndSeedIdentityAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        var seeder = scope.ServiceProvider.GetRequiredService<IdentityDataSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}
