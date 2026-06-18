using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Abstractions;
using Notifications.Domain.Aggregates.DeviceToken;
using Notifications.Domain.Aggregates.Notification;
using Notifications.Infrastructure.Persistence;
using Notifications.Infrastructure.Persistence.Repositories;
using Notifications.Infrastructure.Push;
using Notifications.Infrastructure.Realtime;
using Quartz;

namespace Notifications.Infrastructure;

public static class NotificationsModuleExtensions
{
    public static IServiceCollection AddNotificationsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<NotificationsDbContext>((sp, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<NotificationsDbContext>());
        services.AddScoped<IOutboxWriter>(sp => sp.GetRequiredService<NotificationsDbContext>());
        services.AddScoped<INotificationsDbContext>(sp => sp.GetRequiredService<NotificationsDbContext>());

        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IDeviceTokenRepository, DeviceTokenRepository>();
        services.AddSingleton<INotificationEventBus, NotificationEventBus>();

        // Push fan-out: the FCM transport runs alongside the SignalR transport (registered
        // by NotificationsPresentationExtensions). Handlers depend on the single
        // INotificationPusher contract; CompositeNotificationPusher resolves every
        // registered IInnerNotificationPusher and pushes in parallel.
        services.Configure<FcmOptions>(configuration.GetSection(FcmOptions.SectionName));
        services.AddSingleton<FirebaseAppFactory>();
        services.AddScoped<FcmNotificationPusher>();
        services.AddScoped<IInnerNotificationPusher>(sp => sp.GetRequiredService<FcmNotificationPusher>());
        services.AddScoped<INotificationPusher, CompositeNotificationPusher>();

        services.AddQuartz(q =>
        {
            var key = new JobKey("OutboxProcessor.Notifications");
            q.AddJob<OutboxProcessorJob<NotificationsDbContext>>(opts => opts.WithIdentity(key));
            q.AddTrigger(opts => opts
                .ForJob(key)
                .WithSimpleSchedule(s => s.WithIntervalInSeconds(10).RepeatForever()));
        });

        return services;
    }
}
