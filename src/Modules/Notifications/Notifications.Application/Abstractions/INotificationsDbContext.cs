using BuildingBlocks.Application.Messaging;
using Microsoft.EntityFrameworkCore;
using Notifications.Domain.Devices;
using Notifications.Domain.Notifications;

namespace Notifications.Application.Abstractions;

public interface INotificationsDbContext : IOutboxDbContext
{
    public DbSet<Notification> Notifications { get; }
    public DbSet<DeviceToken> DeviceTokens { get; }
}
