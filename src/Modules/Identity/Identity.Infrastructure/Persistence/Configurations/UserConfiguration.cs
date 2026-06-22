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
            // Only accounts that still hold a login identity participate in uniqueness; a permanently
            // removed account releases its email (LoginEmailReleased = 1) so it can be reused.
            email.HasIndex(e => e.Value)
                .IsUnique()
                .HasFilter("[LoginEmailReleased] = 0");
        });
        builder.Navigation(u => u.Email).IsRequired();

        builder.Property(u => u.DisplayName).HasMaxLength(150).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(512);
        builder.Property(u => u.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(u => u.RoleId).IsRequired();
        builder.Property(u => u.UserType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(u => u.ExternalReferenceId);
        builder.Property(u => u.LoginEmailReleased).IsRequired();
        builder.Property(u => u.PendingEmail).HasMaxLength(256);
        builder.Property(u => u.EmailChangeToken);
        builder.Property(u => u.EmailChangeExpiresAtUtc);
        builder.Property(u => u.SecurityStamp).IsRequired();

        builder.Property(u => u.InvitationToken);
        builder.Property(u => u.InvitationExpiresAtUtc);
        builder.Property(u => u.AccessFailedCount).IsRequired();
        builder.Property(u => u.LockoutEndUtc);
        builder.Property(u => u.CreatedAtUtc).IsRequired();
        builder.Property(u => u.UpdatedAtUtc);
        builder.Property(u => u.LastLoginAtUtc);

        builder.HasIndex(u => u.RoleId);
        builder.HasIndex(u => u.ExternalReferenceId);

        builder.Ignore(u => u.DomainEvents);
    }
}
