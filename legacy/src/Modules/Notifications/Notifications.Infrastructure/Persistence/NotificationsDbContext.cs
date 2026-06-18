using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Infrastructure.Persistence;
using BuildingBlocks.Infrastructure.Persistence.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Abstractions;
using DeviceTokenEntity = Notifications.Domain.Aggregates.DeviceToken.DeviceToken;
using NotificationEntity = Notifications.Domain.Aggregates.Notification.Notification;

namespace Notifications.Infrastructure.Persistence;

public sealed class NotificationsDbContext(
    DbContextOptions<NotificationsDbContext> options,
    ICurrentUserService currentUserService,
    IPublisher publisher)
    : BaseDbContext(options, currentUserService, publisher), INotificationsDbContext
{
    protected override string SchemaName => "notifications";

    public DbSet<NotificationEntity> NotificationItems => Set<NotificationEntity>();
    public DbSet<DeviceTokenEntity> DeviceTokenItems => Set<DeviceTokenEntity>();

    IQueryable<NotificationEntity> INotificationsDbContext.Notifications => NotificationItems;
    IQueryable<DeviceTokenEntity> INotificationsDbContext.DeviceTokens => DeviceTokenItems;

    public async Task<bool> IsAlreadyProcessedAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        await InboxMessages.AnyAsync(m => m.Id == eventId, cancellationToken);

    public void MarkProcessed(Guid eventId, string eventType)
    {
        // We use the integration event's EventId directly as the inbox row's primary key
        // so a re-delivery from the outbox processor causes a unique-key conflict instead
        // of writing duplicate notifications.
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
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationsDbContext).Assembly);
    }
}
