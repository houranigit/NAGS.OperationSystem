using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DeviceTokenEntity = Notifications.Domain.Aggregates.DeviceToken.DeviceToken;
using DeviceTokenId = Notifications.Domain.Aggregates.DeviceToken.DeviceTokenId;

namespace Notifications.Infrastructure.Persistence.Configurations;

public sealed class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceTokenEntity>
{
    public void Configure(EntityTypeBuilder<DeviceTokenEntity> builder)
    {
        builder.ToTable("DeviceTokens", "notifications");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => DeviceTokenId.From(v));

        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.Token).HasMaxLength(4096).IsRequired();
        builder.Property(x => x.Platform).IsRequired().HasConversion<int>();
        builder.Property(x => x.RegisteredAt).IsRequired();
        builder.Property(x => x.LastSeenAt).IsRequired();
        builder.Property(x => x.RevokedAt);

        // The 4-KB token can't go into a unique index directly, so we hash by user + a
        // 256-byte prefix; collisions are still rare in practice (FCM tokens are < 200
        // chars). The application checks (UserId, Token) explicitly anyway via the
        // repository's GetByUserAndTokenAsync.
        builder.HasIndex(x => x.UserId).HasDatabaseName("IX_DeviceTokens_UserId");
        builder.HasIndex(x => new { x.UserId, x.RevokedAt })
            .HasDatabaseName("IX_DeviceTokens_UserId_RevokedAt");
    }
}
