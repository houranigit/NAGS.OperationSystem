using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace BuildingBlocks.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBuildingBlocksInfrastructure(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        services.AddQuartz();
        services.AddQuartzHostedService(opts => opts.WaitForJobsToComplete = true);

        if (configuration is not null)
            services.Configure<EmailSettings>(configuration.GetSection(EmailSettings.SectionName));

        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        return services;
    }
}
