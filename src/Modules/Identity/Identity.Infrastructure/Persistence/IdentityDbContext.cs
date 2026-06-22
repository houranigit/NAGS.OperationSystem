using System.Reflection;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Infrastructure.Messaging;
using Identity.Application.Abstractions;
using Identity.Domain.Roles;
using Identity.Domain.Sessions;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : DbContext(options), IIdentityDbContext, IOutboxDbContext
{
    public const string Schema = "identity";

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserSession> Sessions => Set<UserSession>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        modelBuilder.ApplyOutboxInbox();
        base.OnModelCreating(modelBuilder);
    }
}
