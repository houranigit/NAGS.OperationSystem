using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Operations.Application.Abstractions;
using Operations.Domain.Aggregates.Flight;
using Operations.Domain.Aggregates.WorkOrder;
using Operations.Domain.StationWorkOrderSequence;
using Operations.Infrastructure.BackgroundJobs;
using Operations.Infrastructure.Persistence;
using Operations.Infrastructure.Persistence.Interceptors;
using Operations.Infrastructure.Persistence.Repositories;
using Quartz;

namespace Operations.Infrastructure;

public static class OperationsModuleExtensions
{
    public static IServiceCollection AddOperationsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // The interceptor is the safety-net for the mobile-sync cursor: it stamps
        // UpdatedAt = UtcNow on every modified entity so the "since" query on the
        // catch-up endpoint never misses a row whose aggregate forgot to Touch().
        services.AddSingleton<UpdatedAtInterceptor>();

        services.AddDbContext<OperationsDbContext>((sp, options) =>
            options
                .UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(sp.GetRequiredService<UpdatedAtInterceptor>()));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OperationsDbContext>());
        services.AddScoped<IOutboxWriter>(sp => sp.GetRequiredService<OperationsDbContext>());
        services.AddScoped<IOperationsDbContext>(sp => sp.GetRequiredService<OperationsDbContext>());

        services.AddScoped<IFlightRepository, FlightRepository>();
        services.AddScoped<IWorkOrderRepository, WorkOrderRepository>();
        services.AddScoped<IStationWorkOrderSequenceRepository, StationWorkOrderSequenceRepository>();
        services.AddScoped<Operations.Application.Features.WorkOrder.WorkOrderInputBuilder>();

        var deletionSection = configuration.GetSection(WorkOrderDeletionSettings.SectionName);
        services.Configure<WorkOrderDeletionSettings>(o =>
        {
            o.DelayMinutes = TryParseInt(deletionSection[nameof(WorkOrderDeletionSettings.DelayMinutes)], o.DelayMinutes);
            o.PollIntervalSeconds = TryParseInt(deletionSection[nameof(WorkOrderDeletionSettings.PollIntervalSeconds)], o.PollIntervalSeconds);
            o.BatchSize = TryParseInt(deletionSection[nameof(WorkOrderDeletionSettings.BatchSize)], o.BatchSize);
        });

        var deletionPollSeconds = Math.Max(
            5,
            TryParseInt(
                deletionSection[nameof(WorkOrderDeletionSettings.PollIntervalSeconds)],
                new WorkOrderDeletionSettings().PollIntervalSeconds));

        // Auto-issue settings for AOG flights left untouched past STD + DelayMinutes.
        var autoAogSection = configuration.GetSection(AutoAogWorkOrderSettings.SectionName);
        services.Configure<AutoAogWorkOrderSettings>(o =>
        {
            o.Enabled = TryParseBool(autoAogSection[nameof(AutoAogWorkOrderSettings.Enabled)], o.Enabled);
            o.DelayMinutes = TryParseInt(autoAogSection[nameof(AutoAogWorkOrderSettings.DelayMinutes)], o.DelayMinutes);
            o.PollIntervalSeconds = TryParseInt(autoAogSection[nameof(AutoAogWorkOrderSettings.PollIntervalSeconds)], o.PollIntervalSeconds);
            o.BatchSize = TryParseInt(autoAogSection[nameof(AutoAogWorkOrderSettings.BatchSize)], o.BatchSize);
        });

        var autoAogPollSeconds = Math.Max(
            5,
            TryParseInt(
                autoAogSection[nameof(AutoAogWorkOrderSettings.PollIntervalSeconds)],
                new AutoAogWorkOrderSettings().PollIntervalSeconds));

        services.AddQuartz(q =>
        {
            var outboxKey = new JobKey("OutboxProcessor.Operations");
            q.AddJob<OutboxProcessorJob<OperationsDbContext>>(opts => opts.WithIdentity(outboxKey));
            q.AddTrigger(opts => opts
                .ForJob(outboxKey)
                .WithSimpleSchedule(s => s.WithIntervalInSeconds(10).RepeatForever()));

            var deletionKey = new JobKey("WorkOrderDeletion.Operations");
            q.AddJob<WorkOrderDeletionJob>(opts => opts.WithIdentity(deletionKey));
            q.AddTrigger(opts => opts
                .ForJob(deletionKey)
                .WithSimpleSchedule(s => s.WithIntervalInSeconds(deletionPollSeconds).RepeatForever())
                .StartAt(DateBuilder.FutureDate(deletionPollSeconds, IntervalUnit.Second)));

            var autoAogKey = new JobKey("AutoAogWorkOrder.Operations");
            q.AddJob<AutoAogWorkOrderJob>(opts => opts.WithIdentity(autoAogKey));
            q.AddTrigger(opts => opts
                .ForJob(autoAogKey)
                .WithSimpleSchedule(s => s.WithIntervalInSeconds(autoAogPollSeconds).RepeatForever())
                .StartAt(DateBuilder.FutureDate(autoAogPollSeconds, IntervalUnit.Second)));
        });

        return services;
    }

    private static int TryParseInt(string? raw, int fallback) =>
        int.TryParse(raw, out var v) ? v : fallback;

    private static bool TryParseBool(string? raw, bool fallback) =>
        bool.TryParse(raw, out var v) ? v : fallback;
}
