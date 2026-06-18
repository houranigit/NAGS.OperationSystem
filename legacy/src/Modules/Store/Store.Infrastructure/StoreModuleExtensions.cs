using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Store.Application.Abstractions;
using Store.Contracts.Readers;
using Store.Domain.Aggregates.GeneralSupport;
using Store.Domain.Aggregates.GeneralSupportPricePlan;
using Store.Domain.Aggregates.Material;
using Store.Domain.Aggregates.MaterialPricePlan;
using Store.Domain.Aggregates.Tool;
using Store.Domain.Aggregates.ToolPricePlan;
using Store.Domain.Aggregates.Unit;
using Store.Infrastructure.Persistence;
using Store.Infrastructure.Persistence.Repositories;
using Store.Infrastructure.Readers;

namespace Store.Infrastructure;

public static class StoreModuleExtensions
{
    public static IServiceCollection AddStoreModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<StoreDbContext>((sp, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<StoreDbContext>());
        services.AddScoped<IOutboxWriter>(sp => sp.GetRequiredService<StoreDbContext>());
        services.AddScoped<IStoreDbContext>(sp => sp.GetRequiredService<StoreDbContext>());

        services.AddScoped<IUnitRepository, UnitRepository>();
        services.AddScoped<IToolRepository, ToolRepository>();
        services.AddScoped<IMaterialRepository, MaterialRepository>();
        services.AddScoped<IGeneralSupportRepository, GeneralSupportRepository>();
        services.AddScoped<IToolPricePlanRepository, ToolPricePlanRepository>();
        services.AddScoped<IMaterialPricePlanRepository, MaterialPricePlanRepository>();
        services.AddScoped<IGeneralSupportPricePlanRepository, GeneralSupportPricePlanRepository>();

        services.AddScoped<IUnitReader, UnitReader>();
        services.AddScoped<IToolReader, ToolReader>();
        services.AddScoped<IMaterialReader, MaterialReader>();
        services.AddScoped<IGeneralSupportReader, GeneralSupportReader>();

        services.AddQuartz(q =>
        {
            var jobKey = new JobKey("OutboxProcessor.Store");
            q.AddJob<OutboxProcessorJob<StoreDbContext>>(opts => opts.WithIdentity(jobKey));
            q.AddTrigger(opts => opts
                .ForJob(jobKey)
                .WithSimpleSchedule(s => s.WithIntervalInSeconds(10).RepeatForever()));
        });

        return services;
    }
}
