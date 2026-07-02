using BuildingBlocks.Application.Email;
using BuildingBlocks.Contracts.Email;
using BuildingBlocks.Infrastructure.Messaging;
using BuildingBlocks.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Infrastructure.Email;

public static class DurableEmailExtensions
{
    /// <summary>
    /// Registers durable, retried email delivery: Data Protection for body encryption, the content
    /// protector, and the outbox consumer that performs the actual send.
    /// </summary>
    public static IServiceCollection AddDurableEmail(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var dataProtectionOptions = new OperationsDataProtectionOptions();
        configuration.GetSection(OperationsDataProtectionOptions.SectionName).Bind(dataProtectionOptions);

        services.AddOptions<OperationsDataProtectionOptions>()
            .Bind(configuration.GetSection(OperationsDataProtectionOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<OperationsDataProtectionOptions>>(
                new OperationsDataProtectionOptionsValidator(environment)));

        var builder = services.AddDataProtection()
            .SetApplicationName(dataProtectionOptions.ApplicationName);

        if (!string.IsNullOrWhiteSpace(dataProtectionOptions.KeyRingPath))
        {
            var keyRingPath = Path.GetFullPath(dataProtectionOptions.KeyRingPath);
            Directory.CreateDirectory(keyRingPath);
            builder.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        }

        services.TryAddSingleton<IEmailContentProtector, DataProtectionEmailContentProtector>();
        services.AddIntegrationEventHandler<EmailDeliveryRequested, EmailDeliveryRequestedHandler>();
        return services;
    }
}
