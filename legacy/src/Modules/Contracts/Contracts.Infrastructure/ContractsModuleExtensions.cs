using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Infrastructure.Outbox;
using Contracts.Application.Abstractions;
using Contracts.Application.Features.Contract.Shared;
using Contracts.Contracts.Readers;
using Contracts.Domain.Aggregates.Contract;
using Contracts.Infrastructure.BackgroundJobs;
using Contracts.Infrastructure.Persistence;
using Contracts.Infrastructure.Persistence.Repositories;
using Contracts.Infrastructure.Readers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Contracts.Infrastructure;

public static class ContractsModuleExtensions
{
    public static IServiceCollection AddContractsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<ContractsDbContext>((sp, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ContractsDbContext>());
        services.AddScoped<IOutboxWriter>(sp => sp.GetRequiredService<ContractsDbContext>());
        services.AddScoped<IContractsDbContext>(sp => sp.GetRequiredService<ContractsDbContext>());

        services.AddScoped<IContractRepository, ContractRepository>();
        services.AddScoped<ContractDraftBuilder>();
        services.AddScoped<IContractReadService, ContractReadService>();

        services.TryAddSingletonTimeProvider();

        services.AddQuartz(q =>
        {
            var outboxKey = new JobKey("OutboxProcessor.Contracts");
            q.AddJob<OutboxProcessorJob<ContractsDbContext>>(opts => opts.WithIdentity(outboxKey));
            q.AddTrigger(opts => opts
                .ForJob(outboxKey)
                .WithSimpleSchedule(s => s.WithIntervalInSeconds(10).RepeatForever()));

            var statusKey = new JobKey("ContractStatusSync.Contracts");
            q.AddJob<ContractStatusSyncJob>(opts => opts.WithIdentity(statusKey));
            q.AddTrigger(opts => opts
                .ForJob(statusKey)
                .WithSimpleSchedule(s => s.WithIntervalInMinutes(5).RepeatForever())
                .StartAt(DateBuilder.FutureDate(30, IntervalUnit.Second)));

            var alertKey = new JobKey("ContractExpiringNotification.Contracts");
            q.AddJob<ContractExpiringNotificationJob>(opts => opts.WithIdentity(alertKey));
            q.AddTrigger(opts => opts
                .ForJob(alertKey)
                .WithSimpleSchedule(s => s.WithIntervalInHours(6).RepeatForever())
                .StartAt(DateBuilder.FutureDate(60, IntervalUnit.Second)));
        });

        return services;
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (services.Any(s => s.ServiceType == typeof(TimeProvider))) return;
        services.AddSingleton(TimeProvider.System);
    }
}
