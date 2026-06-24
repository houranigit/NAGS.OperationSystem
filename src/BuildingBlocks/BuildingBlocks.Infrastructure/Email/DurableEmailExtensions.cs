using BuildingBlocks.Application.Email;
using BuildingBlocks.Contracts.Email;
using BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Infrastructure.Email;

public static class DurableEmailExtensions
{
    /// <summary>
    /// Registers durable, retried email delivery: Data Protection for body encryption, the content
    /// protector, and the outbox consumer that performs the actual send. Persistent Data Protection
    /// key storage is configured in the release-hardening phase.
    /// </summary>
    public static IServiceCollection AddDurableEmail(this IServiceCollection services)
    {
        services.AddDataProtection();
        services.TryAddSingleton<IEmailContentProtector, DataProtectionEmailContentProtector>();
        services.AddIntegrationEventHandler<EmailDeliveryRequested, EmailDeliveryRequestedHandler>();
        return services;
    }
}
