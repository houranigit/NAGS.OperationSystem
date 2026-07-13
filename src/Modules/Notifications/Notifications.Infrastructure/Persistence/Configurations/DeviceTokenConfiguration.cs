using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notifications.Domain.Devices;

namespace Notifications.Infrastructure.Persistence.Configurations;

public sealed class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> builder)
    {
        builder.ToTable("device_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.UserId).IsRequired();
        builder.Property(t => t.Token).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(t => t.Platform).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.DeviceId).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Locale).HasMaxLength(10).IsUnicode(false).IsRequired();
        builder.Property(t => t.AppVersion).HasMaxLength(50);
        builder.Property(t => t.RegisteredAtUtc).IsRequired();
        builder.Property(t => t.LastSeenAtUtc).IsRequired();
        builder.Property(t => t.RevokedAtUtc);

        // An FCM registration token belongs to one physical app installation at a time. Global
        // uniqueness prevents pushes leaking to a prior account after an account switch.
        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("UX_device_tokens_token_hash");
        builder.HasIndex(t => t.DeviceId).IsUnique().HasDatabaseName("UX_device_tokens_device_id");
        builder.HasIndex(t => new { t.UserId, t.RevokedAtUtc })
            .HasDatabaseName("IX_device_tokens_user_active");

        builder.Ignore(t => t.IsActive);
        builder.Ignore(t => t.DomainEvents);
    }
}
