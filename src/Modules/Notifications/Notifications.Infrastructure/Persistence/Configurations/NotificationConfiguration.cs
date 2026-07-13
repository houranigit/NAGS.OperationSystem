using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notifications.Domain.Notifications;

namespace Notifications.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.RecipientUserId).IsRequired();
        builder.Property(n => n.Kind).HasMaxLength(64).IsRequired();
        builder.Property(n => n.TitleEn).HasMaxLength(200).IsRequired();
        builder.Property(n => n.BodyEn).HasMaxLength(500).IsRequired();
        builder.Property(n => n.TitleAr).HasMaxLength(200).IsRequired();
        builder.Property(n => n.BodyAr).HasMaxLength(500).IsRequired();
        builder.Property(n => n.PayloadJson).IsRequired();
        builder.Property(n => n.CreatedAtUtc).IsRequired();
        builder.Property(n => n.DeliveredAtUtc);
        builder.Property(n => n.ReadAtUtc);
        builder.Property(n => n.ArchivedAtUtc);

        builder.HasIndex(n => new { n.RecipientUserId, n.CreatedAtUtc })
            .HasDatabaseName("IX_notifications_recipient_created")
            .IsDescending(false, true)
            .HasFilter("[ArchivedAtUtc] IS NULL");
        builder.HasIndex(n => new { n.RecipientUserId, n.ReadAtUtc, n.CreatedAtUtc })
            .HasDatabaseName("IX_notifications_recipient_unread_created")
            .HasFilter("[ArchivedAtUtc] IS NULL");

        builder.Ignore(n => n.IsRead);
        builder.Ignore(n => n.IsArchived);
        builder.Ignore(n => n.DomainEvents);
    }
}
