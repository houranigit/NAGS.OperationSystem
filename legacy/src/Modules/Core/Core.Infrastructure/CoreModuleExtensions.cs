using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Infrastructure.Outbox;
using Core.Application.Abstractions;
using Core.Contracts.Readers;
using Core.Domain.Aggregates.AircraftType;
using Core.Domain.Aggregates.Country;
using Core.Domain.Aggregates.Currency;
using Core.Domain.Aggregates.Customer;
using Core.Domain.Aggregates.Employee;
using Core.Domain.Aggregates.License;
using Core.Domain.Aggregates.ManpowerPricePlan;
using Core.Domain.Aggregates.ManpowerType;
using Core.Domain.Aggregates.OperationType;
using Core.Domain.Aggregates.Service;
using Core.Domain.Aggregates.ServicePricePlan;
using Core.Domain.Aggregates.Station;
using Core.Infrastructure.Persistence;
using Core.Infrastructure.Persistence.Repositories;
using Core.Infrastructure.Readers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Core.Infrastructure;

public static class CoreModuleExtensions
{
    public static IServiceCollection AddCoreModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<CoreDbContext>((sp, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CoreDbContext>());
        services.AddScoped<IOutboxWriter>(sp => sp.GetRequiredService<CoreDbContext>());
        services.AddScoped<ICoreDbContext>(sp => sp.GetRequiredService<CoreDbContext>());

        services.AddScoped<IAircraftTypeRepository, AircraftTypeRepository>();
        services.AddScoped<ICountryRepository, CountryRepository>();
        services.AddScoped<ICurrencyRepository, CurrencyRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<ILicenseRepository, LicenseRepository>();
        services.AddScoped<IManpowerTypeRepository, ManpowerTypeRepository>();
        services.AddScoped<IOperationTypeRepository, OperationTypeRepository>();
        services.AddScoped<IServiceRepository, ServiceRepository>();
        services.AddScoped<IStationRepository, StationRepository>();
        services.AddScoped<IManpowerPricePlanRepository, ManpowerPricePlanRepository>();
        services.AddScoped<IServicePricePlanRepository, ServicePricePlanRepository>();

        services.AddScoped<ICustomerReader, CustomerReader>();
        services.AddScoped<ICurrencyReader, CurrencyReader>();
        services.AddScoped<IStationReader, StationReader>();
        services.AddScoped<IOperationTypeReader, OperationTypeReader>();
        services.AddScoped<IServiceReader, ServiceReader>();
        services.AddScoped<IManpowerTypeReader, ManpowerTypeReader>();
        services.AddScoped<IAircraftTypeReader, AircraftTypeReader>();
        services.AddScoped<IEmployeeReader, EmployeeReader>();

        services.AddQuartz(q =>
        {
            var jobKey = new JobKey("OutboxProcessor.Core");
            q.AddJob<OutboxProcessorJob<CoreDbContext>>(opts => opts.WithIdentity(jobKey));
            q.AddTrigger(opts => opts
                .ForJob(jobKey)
                .WithSimpleSchedule(s => s.WithIntervalInSeconds(10).RepeatForever()));
        });

        return services;
    }
}
