using BuildingBlocks.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Infrastructure.Email;

public static class EmailExtensions
{
    public static IServiceCollection AddEmailSender(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateOnStart();

        services.TryAddSingleton<IEmailSender, SmtpEmailSender>();
        return services;
    }
}
