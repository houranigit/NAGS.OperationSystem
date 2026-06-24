using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Email;
using BuildingBlocks.Contracts.Email;
using BuildingBlocks.Contracts.Messaging;

namespace BuildingBlocks.Infrastructure.Email;

/// <summary>
/// Sends a queued email when its <see cref="EmailDeliveryRequested"/> is dispatched from a module
/// outbox. The body is decrypted only here, at the moment of sending. A send failure throws so the
/// outbox processor retries on its next cycle (durable, at-least-once delivery).
/// </summary>
public sealed class EmailDeliveryRequestedHandler(IEmailSender sender, IEmailContentProtector protector)
    : IIntegrationEventHandler<EmailDeliveryRequested>
{
    public async Task HandleAsync(EmailDeliveryRequested integrationEvent, CancellationToken cancellationToken = default)
    {
        var body = protector.Unprotect(integrationEvent.ProtectedBody);
        await sender.SendAsync(
            new EmailMessage(integrationEvent.ToEmail, integrationEvent.ToName, integrationEvent.Subject, body),
            cancellationToken);
    }
}
