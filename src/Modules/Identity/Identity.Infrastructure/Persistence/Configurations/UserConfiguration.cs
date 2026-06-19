using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        builder.OwnsOne(u => u.Email, email =>
        {
            email.Property(e => e.Value)
                .HasColumnName("Email")
                .HasMaxLength(256)
                .IsRequired();
            email.HasIndex(e => e.Value).IsUnique();
        });
        builder.Navigation(u => u.Email).IsRequired();

        builder.Property(u => u.DisplayName).HasMaxLength(150).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(512);
        builder.Property(u => u.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(u => u.RoleId).IsRequired();
        builder.Property(u => u.SecurityStamp).IsRequired();

        builder.Property(u => u.InvitationToken);
        builder.Property(u => u.InvitationExpiresAtUtc);
        builder.Property(u => u.AccessFailedCount).IsRequired();
        builder.Property(u => u.LockoutEndUtc);
        builder.Property(u => u.CreatedAtUtc).IsRequired();
        builder.Property(u => u.UpdatedAtUtc);
        builder.Property(u => u.LastLoginAtUtc);

        builder.HasIndex(u => u.RoleId);

        builder.Ignore(u => u.DomainEvents);
    }
}
