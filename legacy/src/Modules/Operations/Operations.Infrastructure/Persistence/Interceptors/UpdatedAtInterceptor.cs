using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Operations.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Belt-and-braces guard for the per-table monotonic cursor used by the mobile
/// real-time sync. Both <c>Flight</c> and <c>WorkOrder</c> already self-stamp
/// <c>UpdatedAt</c> via their private <c>Touch()</c> methods on every mutation; this
/// interceptor catches the case where a future handler forgets — or a future entity
/// is added with the same shape — and stamps <c>UpdatedAt</c> at the
/// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> boundary so the
/// catch-up endpoint can rely on "everything modified since <c>since</c>" returning
/// every change.
/// </summary>
/// <remarks>
/// We scan reflectively for a writable <c>UpdatedAt</c> property of type
/// <see cref="DateTimeOffset"/> on each modified entity and cache the resolved
/// <see cref="PropertyInfo"/> per-CLR-type so the hot path is a dictionary lookup
/// per save. Entities that don't have the property are cached as <c>null</c> and
/// skipped on subsequent saves.
/// </remarks>
public sealed class UpdatedAtInterceptor : SaveChangesInterceptor
{
    private const string UpdatedAtPropertyName = "UpdatedAt";

    private static readonly ConcurrentDictionary<Type, PropertyInfo?> PropertyCache = new();

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
            StampModifiedEntities(context, DateTimeOffset.UtcNow);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
            StampModifiedEntities(context, DateTimeOffset.UtcNow);

        return base.SavingChanges(eventData, result);
    }

    private static void StampModifiedEntities(DbContext context, DateTimeOffset now)
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            var property = PropertyCache.GetOrAdd(entry.Entity.GetType(), ResolveUpdatedAtProperty);
            if (property is null)
                continue;

            // Only overwrite UpdatedAt if the aggregate hasn't already touched it during
            // this unit of work. Domain Touch() runs as part of the operation and EF
            // marks the property as Modified; we only stamp when the property isn't
            // already in the modified-properties set so we don't clobber a deliberate
            // historical timestamp written via reflection / migration code.
            var efEntry = entry.Property(UpdatedAtPropertyName);
            if (efEntry.IsModified) continue;

            property.SetValue(entry.Entity, now);
            efEntry.IsModified = true;
        }
    }

    private static PropertyInfo? ResolveUpdatedAtProperty(Type type)
    {
        var prop = type.GetProperty(
            UpdatedAtPropertyName,
            BindingFlags.Public | BindingFlags.Instance);

        if (prop is null || prop.PropertyType != typeof(DateTimeOffset))
            return null;

        // The aggregates expose UpdatedAt with a private setter — reflection sees
        // CanWrite=true only when we ask for the non-public set method, which is
        // what GetSetMethod(true) returns. If even that's missing we skip.
        return prop.GetSetMethod(nonPublic: true) is null ? null : prop;
    }
}
