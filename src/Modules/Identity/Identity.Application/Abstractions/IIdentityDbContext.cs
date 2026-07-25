using BuildingBlocks.Application.Messaging;
using Identity.Domain.Roles;
using Identity.Domain.Sessions;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Abstractions;

/// <summary>
/// Application-facing view of the Identity persistence store. Implemented by the module's
/// EF Core DbContext in Infrastructure so handlers can query/persist without depending on
/// Infrastructure directly. Extends <see cref="IOutboxDbContext"/> so integration-event handlers
/// can dedupe via the inbox and enqueue replies in the same transaction.
/// </summary>
public interface IIdentityDbContext : IOutboxDbContext
{
    public DbSet<User> Users { get; }
    public DbSet<Role> Roles { get; }
    public DbSet<UserSession> Sessions { get; }

    /// <summary>
    /// Serializes access-management workflows so protected-administrator checks and their writes
    /// observe one stable set of users.
    /// </summary>
    public Task<IIdentityTransaction> BeginAccessManagementTransactionAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a transaction serialized with refresh/revocation operations for one sign-in lineage.
    /// This avoids a global refresh bottleneck while making logout and token rotation linearizable.
    /// </summary>
    public Task<IIdentityTransaction> BeginSessionFamilyTransactionAsync(
        Guid familyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires a session-family lock inside an already-open transaction (for privileged workflows
    /// that first need the broader access-management lock).
    /// </summary>
    public Task AcquireSessionFamilyLockAsync(
        Guid familyId,
        CancellationToken cancellationToken = default);

    /// <summary>Sets the original concurrency token supplied by the caller.</summary>
    public void SetOriginalRowVersion<TEntity>(TEntity entity, byte[] rowVersion) where TEntity : class;

    /// <summary>
    /// Reloads a tracked entity after acquiring a serialized access-management lock so decisions
    /// are made from the latest committed state rather than a pre-lock authentication snapshot.
    /// </summary>
    public Task ReloadAsync<TEntity>(
        TEntity entity,
        CancellationToken cancellationToken = default)
        where TEntity : class;
}

public interface IIdentityTransaction : IAsyncDisposable
{
    public Task CommitAsync(CancellationToken cancellationToken = default);
}
