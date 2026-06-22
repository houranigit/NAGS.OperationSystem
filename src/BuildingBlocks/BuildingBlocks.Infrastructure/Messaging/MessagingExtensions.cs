using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Contracts.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;

namespace BuildingBlocks.Infrastructure.Messaging;

public static class MessagingExtensions
{
    /// <summary>
    /// Registers the in-process integration-event dispatcher and the Quartz job that drains module
    /// outboxes on an interval. Call once at host composition, then <see cref="AddModuleOutbox{T}"/>
    /// for each module DbContext.
    /// </summary>
    public static IServiceCollection AddIntegrationMessaging(this IServiceCollection services, int pollSeconds = 10)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IIntegrationEventDispatcher, InProcessIntegrationEventDispatcher>();

        services.AddQuartz(q =>
        {
            q.AddJob<OutboxDispatchJob>(j => j.WithIdentity(OutboxDispatchJob.Key));
            q.AddTrigger(t => t
                .ForJob(OutboxDispatchJob.Key)
                .WithIdentity("outbox-dispatch-trigger")
                .WithSimpleSchedule(s => s.WithIntervalInSeconds(pollSeconds).RepeatForever()));
        });

        services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
        return services;
    }

    /// <summary>Registers a module's outbox processor so the dispatch job drains its outbox.</summary>
    public static IServiceCollection AddModuleOutbox<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext, IOutboxDbContext
    {
        services.AddScoped<IOutboxProcessor, OutboxProcessor<TDbContext>>();
        return services;
    }

    /// <summary>
    /// Registers an in-process consumer for an integration event. The dispatcher resolves every
    /// registered <see cref="IIntegrationEventHandler{TEvent}"/> when draining an outbox, so a handler
    /// may live in any module that references the event's contract.
    /// </summary>
    public static IServiceCollection AddIntegrationEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        services.AddScoped<IIntegrationEventHandler<TEvent>, THandler>();
        return services;
    }
}
