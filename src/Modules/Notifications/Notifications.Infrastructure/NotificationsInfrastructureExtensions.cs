using BuildingBlocks.Infrastructure.Messaging;
using MasterData.Contracts.Readers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Notifications.Application.Abstractions;
using Notifications.Application.IntegrationEvents;
using Notifications.Infrastructure.Persistence;
using Notifications.Infrastructure.Push;
using Operations.Contracts;

namespace Notifications.Infrastructure;

public static class NotificationsInfrastructureExtensions
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Notifications")
            ?? configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("No 'Notifications' or 'Default' connection string configured.");

        services.AddDbContext<NotificationsDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", NotificationsDbContext.Schema)));
        services.AddScoped<INotificationsDbContext>(provider => provider.GetRequiredService<NotificationsDbContext>());
        services.AddModuleOutbox<NotificationsDbContext>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IValidateOptions<FcmOptions>, FcmOptionsValidator>();
        services.AddOptions<FcmOptions>()
            .Bind(configuration.GetSection(FcmOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<FirebaseAppFactory>();
        services.AddHostedService<FcmStartupValidator>();
        services.AddScoped<INotificationTransport, FcmNotificationPusher>();
        services.AddScoped<INotificationPusher, CompositeNotificationPusher>();

        services.AddIntegrationEventHandler<FlightEmployeeAssigned, FlightEmployeeAssignedHandler>();
        services.AddIntegrationEventHandler<FlightScheduleUpdated, FlightScheduleUpdatedHandler>();
        services.AddIntegrationEventHandler<FlightReminderDue, FlightReminderDueHandler>();
        return services;
    }

    public static async Task MigrateNotificationsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
