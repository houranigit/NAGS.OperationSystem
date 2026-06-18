using BuildingBlocks.Application.Abstractions;
using Identity.Domain.Aggregates.Role;
using Identity.Domain.Aggregates.User;

namespace Identity.Application.Abstractions;

/// <summary>
/// Application-side surface for the Identity module's persistence — exposes inbox-dedup hooks
/// used by integration event handlers (mirrors <c>INotificationsDbContext</c>) and the SaveChanges
/// hook from <see cref="IUnitOfWork"/>. Avoids leaking the EF DbContext into Application.
/// </summary>
public interface IIdentityDbContext : IUnitOfWork
{
    /// <summary>Read-only query surface for User — paginated grid handlers project to DTOs.</summary>
    IQueryable<User> Users { get; }

    /// <summary>Read-only query surface for Role — used to project role names alongside users.</summary>
    IQueryable<Role> Roles { get; }

    /// <summary>True if an inbox row already exists for the given EventId — handlers should short-circuit.</summary>
    Task<bool> IsAlreadyProcessedAsync(Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an inbox row keyed by <paramref name="eventId"/>. The next SaveChanges persists it; a
    /// re-delivery raises a unique-key conflict so the handler is naturally idempotent.
    /// </summary>
    void MarkProcessed(Guid eventId, string eventType);
}
