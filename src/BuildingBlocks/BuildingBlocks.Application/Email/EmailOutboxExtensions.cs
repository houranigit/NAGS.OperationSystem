using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Contracts.Email;

namespace BuildingBlocks.Application.Email;

/// <summary>Enqueues a durable email into the calling module's outbox, with its body encrypted.</summary>
public static class EmailOutboxExtensions
{
    public static void EnqueueEmail(
        this IOutboxDbContext db,
        IEmailContentProtector protector,
        EmailMessage message,
        string? kind = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(message);

        db.Enqueue(new EmailDeliveryRequested
        {
            ToEmail = message.ToEmail,
            ToName = message.ToName,
            Subject = message.Subject,
            ProtectedBody = protector.Protect(message.HtmlBody),
            Kind = kind
        });
    }
}
