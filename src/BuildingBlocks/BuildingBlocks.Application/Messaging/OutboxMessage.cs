using System.Text.Json;
using BuildingBlocks.Contracts.Messaging;

namespace BuildingBlocks.Application.Messaging;

/// <summary>
/// A transactional record of an integration event to publish. Written to the originating module's
/// schema in the same transaction as the state change; an outbox processor dispatches it after
/// commit. <see cref="Type"/> is the assembly-qualified CLR type name used to rehydrate the event.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }
    public DateTimeOffset OccurredOnUtc { get; init; }
    public string Type { get; init; } = null!;
    public string Content { get; init; } = null!;
    public DateTimeOffset? ProcessedOnUtc { get; set; }
    public int Attempts { get; set; }
    public string? Error { get; set; }

    public static OutboxMessage Create(IntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return new OutboxMessage
        {
            Id = integrationEvent.EventId,
            OccurredOnUtc = integrationEvent.OccurredOnUtc,
            Type = integrationEvent.GetType().AssemblyQualifiedName!,
            Content = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType())
        };
    }
}
