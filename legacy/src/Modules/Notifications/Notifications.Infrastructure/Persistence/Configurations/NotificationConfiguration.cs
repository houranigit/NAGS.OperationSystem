using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationEntity = Notifications.Domain.Aggregates.Notification.Notification;
using NotificationId = Notifications.Domain.Aggregates.Notification.NotificationId;

namespace Notifications.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<NotificationEntity>
{
    public void Configure(EntityTypeBuilder<NotificationEntity> builder)
    {
        builder.ToTable("Notifications", "notifications");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => NotificationId.From(v));

        builder.Property(x => x.RecipientUserId).IsRequired();
        builder.Property(x => x.Kind).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(500).IsRequired();
        builder.Property(x => x.PayloadJson).IsRequired();
        builder.Property(x => x.IsRead).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.ReadAt);
        builder.Property(x => x.IsArchived).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.ArchivedAt);

        // Filtered index: only non-archived rows participate in inbox / unread-count
        // queries, so this is the hot index for mobile + portal bell. Archived rows
        // are kept around for audit but never make it into the main listings.
        builder.HasIndex(x => new { x.RecipientUserId, x.IsRead, x.CreatedAt })
            .HasDatabaseName("IX_Notifications_Recipient_IsRead_CreatedAt")
            .HasFilter("[IsArchived] = 0");
    }
}
