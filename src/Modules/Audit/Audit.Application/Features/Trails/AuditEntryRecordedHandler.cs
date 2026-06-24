using System.Text.Json;
using Audit.Application.Abstractions;
using Audit.Domain.Trails;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Contracts.Auditing;
using BuildingBlocks.Contracts.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Audit.Application.Features.Trails;

/// <summary>
/// Persists an <see cref="AuditEntryRecorded"/> into the permanent trail. Idempotent via the module
/// inbox so the at-least-once outbox dispatcher cannot create duplicate rows.
/// </summary>
public sealed class AuditEntryRecordedHandler(IAuditDbContext db, TimeProvider timeProvider)
    : IIntegrationEventHandler<AuditEntryRecorded>
{
    private const string Consumer = "audit.trails";

    public async Task HandleAsync(AuditEntryRecorded integrationEvent, CancellationToken cancellationToken = default)
    {
        if (await db.HasProcessedAsync(integrationEvent.EventId, Consumer, cancellationToken))
            return;

        var changesJson = integrationEvent.Changes.Count == 0
            ? null
            : JsonSerializer.Serialize(integrationEvent.Changes);

        var trail = AuditTrail.Create(
            integrationEvent.EventId,
            integrationEvent.OccurredOnUtc,
            integrationEvent.ActorId,
            integrationEvent.ActorDisplayName,
            integrationEvent.IsSystemActor,
            integrationEvent.Module,
            integrationEvent.RootSubjectType,
            integrationEvent.RootSubjectId,
            integrationEvent.EntityType,
            integrationEvent.EntityId,
            integrationEvent.Action,
            integrationEvent.CorrelationId,
            changesJson,
            integrationEvent.Metadata);

        db.AuditTrails.Add(trail);
        db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
        await db.SaveChangesAsync(cancellationToken);
    }
}
