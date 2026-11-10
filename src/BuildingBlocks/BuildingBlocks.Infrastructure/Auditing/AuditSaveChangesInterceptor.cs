using System.Globalization;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Contracts.Auditing;
using BuildingBlocks.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BuildingBlocks.Infrastructure.Auditing;

/// <summary>
/// Captures create/update changes of <see cref="IAuditable"/> entities into the module's outbox in
/// the same transaction as the change. The Audit module persists the resulting events, so a business
/// change can never commit without a durable audit record. Secrets are dropped via
/// <see cref="AuditRedaction"/>; if every changed property is sensitive, no entry is produced.
/// </summary>
public sealed class AuditSaveChangesInterceptor(IAuditContext auditContext) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Capture(DbContext? context)
    {
        if (context is not IOutboxDbContext outbox)
            return;

        var module = context.Model.GetDefaultSchema() ?? context.GetType().Name;

        // Snapshot first: we add OutboxMessage entities below, which would mutate the entry set.
        var auditable = context.ChangeTracker.Entries()
            .Where(e => e.Entity is IAuditable && (e.State == EntityState.Added || e.State == EntityState.Modified))
            .ToList();

        var events = new List<AuditEntryRecorded>();

        foreach (var entry in auditable)
        {
            var entity = (IAuditable)entry.Entity;
            var isCreate = entry.State == EntityState.Added;
            var changes = new List<AuditFieldChange>();

            foreach (var property in entry.Properties)
            {
                var name = property.Metadata.Name;
                if (AuditRedaction.IsSensitive(name))
                    continue;

                if (isCreate)
                {
                    if (property.CurrentValue is not null)
                        changes.Add(new AuditFieldChange(name, null, Format(property.CurrentValue)));
                }
                else if (property.IsModified && !Equals(property.OriginalValue, property.CurrentValue))
                {
                    changes.Add(new AuditFieldChange(name, Format(property.OriginalValue), Format(property.CurrentValue)));
                }
            }

            var complexProperties = entry.ComplexProperties
                .Where(property => !AuditRedaction.IsSensitive(property.Metadata.Name)).ToList();
            if (complexProperties.Count > 0)
            {
                // Scalar defaults cannot distinguish a missing complex value from a value containing
                // zero/false. Reconstruct the tracked snapshot to retain original parent presence.
                var originalEntity = isCreate ? null : entry.OriginalValues.ToObject();
                foreach (var complexProperty in complexProperties)
                {
                    foreach (var (name, before, after) in ComplexScalarValues(complexProperty,
                        complexProperty.Metadata.Name, originalEntity, entry.Entity))
                    {
                        if (isCreate)
                        {
                            if (after is not null)
                                changes.Add(new AuditFieldChange(name, null, Format(after)));
                        }
                        else if (!Equals(before, after))
                        {
                            changes.Add(new AuditFieldChange(name, Format(before), Format(after)));
                        }
                    }
                }
            }

            // A modify whose only changes are secrets (e.g. just a security-stamp rotation) is not
            // recorded by automatic capture; explicit security events cover those.
            if (!isCreate && changes.Count == 0)
                continue;

            events.Add(new AuditEntryRecorded
            {
                ActorId = auditContext.ActorId,
                ActorDisplayName = auditContext.ActorDisplayName,
                IsSystemActor = auditContext.IsSystemActor,
                Module = module,
                RootSubjectType = entity.AuditRootType,
                RootSubjectId = entity.AuditRootId,
                EntityType = entity.AuditEntityType,
                EntityId = entity.AuditEntityId,
                Action = isCreate ? AuditActions.Created : AuditActions.Updated,
                CorrelationId = auditContext.CorrelationId,
                Changes = changes
            });
        }

        foreach (var captured in events)
            outbox.OutboxMessages.Add(OutboxMessage.Create(captured));
    }

    private static IEnumerable<(string Name, object? Before, object? After)> ComplexScalarValues(
        ComplexPropertyEntry entry, string path, object? originalParent, object? currentParent)
    {
        // A sensitive parent excludes its complete value, including nested scalar fields.
        if (AuditRedaction.IsSensitive(path))
            yield break;

        var getter = entry.Metadata.GetGetter();
        var originalValue = originalParent is null ? null : getter.GetClrValue(originalParent);
        var currentValue = currentParent is null ? null : getter.GetClrValue(currentParent);
        foreach (var property in entry.Properties)
        {
            var name = $"{path}.{property.Metadata.Name}";
            if (AuditRedaction.IsSensitive(name))
                continue;

            yield return (name,
                originalValue is null ? null : property.OriginalValue,
                currentValue is null ? null : property.CurrentValue);
        }

        foreach (var complexProperty in entry.ComplexProperties)
        {
            foreach (var value in ComplexScalarValues(complexProperty,
                $"{path}.{complexProperty.Metadata.Name}", originalValue, currentValue))
                yield return value;
        }
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}
