using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Infrastructure.Auditing;
using BuildingBlocks.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Features.Flights;
using Operations.Infrastructure.Persistence;

namespace Operations.Infrastructure;

public static class OperationsInfrastructureExtensions
{
    public static IServiceCollection AddOperationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Operations")
            ?? configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("No 'Operations' or 'Default' connection string configured.");

        services.AddDbContext<OperationsDbContext>((sp, options) =>
        {
            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", OperationsDbContext.Schema));

            if (sp.GetService<AuditSaveChangesInterceptor>() is { } auditInterceptor)
                options.AddInterceptors(auditInterceptor);
        });

        services.AddScoped<IOperationsDbContext>(sp => sp.GetRequiredService<OperationsDbContext>());
        services.AddScoped<IOperationsScope, OperationsScope>();
        services.AddScoped<MasterDataResolver>();
        services.AddScoped<IFlightTimelineWriter, FlightTimelineWriter>();
        services.AddScoped<FlightDuplicateDetector>();

        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IPermissionCatalog, OperationsPermissionCatalog>();
        services.AddModuleOutbox<OperationsDbContext>();

        return services;
    }

    public static async Task MigrateOperationsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
