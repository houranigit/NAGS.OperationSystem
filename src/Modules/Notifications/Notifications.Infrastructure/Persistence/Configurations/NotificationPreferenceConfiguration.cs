using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notifications.Domain.Preferences;

namespace Notifications.Infrastructure.Persistence.Configurations;

public sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("notification_preferences");
        builder.HasKey(preference => new { preference.UserId, preference.Key });
        builder.Property(preference => preference.Key).HasMaxLength(40).IsUnicode(false);
        builder.Property(preference => preference.Enabled).IsRequired();
    }
}
