using Identity.Domain.Aggregates.User;
using Identity.Domain.Aggregates.UserSession;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

public sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("UserSessions", "identity");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => UserSessionId.From(v));

        builder.Property(x => x.UserId)
            .HasConversion(v => v.Value, v => UserId.From(v));

        builder.Property(x => x.AccessToken).IsRequired().HasMaxLength(2000);
        builder.Property(x => x.RefreshToken).IsRequired().HasMaxLength(500);
        builder.HasIndex(x => x.RefreshToken).IsUnique();

        builder.Property(x => x.DeviceInfo).HasMaxLength(500);
        builder.Property(x => x.IpAddress).HasMaxLength(50);
        builder.Property(x => x.UserAgent).HasMaxLength(500);
        builder.Property(x => x.RevokedReason).HasMaxLength(200);
    }
}
