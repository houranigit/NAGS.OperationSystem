using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Infrastructure.Persistence;
using BuildingBlocks.Infrastructure.Persistence.Models;
using Identity.Application.Abstractions;
using Identity.Domain.Aggregates.Role;
using Identity.Domain.Aggregates.User;
using Identity.Domain.Aggregates.UserSession;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext(
    DbContextOptions<IdentityDbContext> options,
    ICurrentUserService currentUserService,
    IPublisher publisher)
    : BaseDbContext(options, currentUserService, publisher), IIdentityDbContext
{
    protected override string SchemaName => "identity";

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<PasswordHistoryEntry> PasswordHistory => Set<PasswordHistoryEntry>();

    IQueryable<User> IIdentityDbContext.Users => Users.AsNoTracking();
    IQueryable<Role> IIdentityDbContext.Roles => Roles.AsNoTracking();

    public async Task<bool> IsAlreadyProcessedAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        await InboxMessages.AnyAsync(m => m.Id == eventId, cancellationToken);

    public void MarkProcessed(Guid eventId, string eventType)
    {
        // PK == EventId so a re-delivery from any module's outbox processor raises a unique-key
        // conflict instead of double-creating the user / re-sending the invitation email.
        InboxMessages.Add(new InboxMessage
        {
            Id = eventId,
            Type = eventType,
            ReceivedAt = DateTime.UtcNow,
            ProcessedAt = DateTime.UtcNow
        });
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
    }
}
