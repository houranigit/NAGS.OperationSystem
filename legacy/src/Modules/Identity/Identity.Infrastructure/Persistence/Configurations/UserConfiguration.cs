using BuildingBlocks.Domain.Enumerations;
using Identity.Domain.Aggregates.Role;
using Identity.Domain.Aggregates.User;
using Identity.Domain.Enumerations;
using Identity.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", "identity");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => UserId.From(v));

        // OwnsOne (not HasConversion) so the underlying string is reachable from LINQ as
        // x.Username.Value / x.Email.Value — Radzen DataGrid filter expressions need a string-
        // typed leaf, otherwise "(Username ?? \"\").ToLower().Contains(...)" can't typecheck
        // against the VO type. Same shape used in Core.StationConfiguration / FlightConfiguration.
        builder.OwnsOne(x => x.Username, un =>
        {
            un.Property(v => v.Value)
                .HasColumnName("Username")
                .HasMaxLength(50)
                .IsRequired();

            un.HasIndex(v => v.Value).IsUnique();
        });
        builder.Navigation(x => x.Username).IsRequired();

        builder.OwnsOne(x => x.Email, em =>
        {
            em.Property(v => v.Value)
                .HasColumnName("Email")
                .HasMaxLength(254)
                .IsRequired();

            em.HasIndex(v => v.Value).IsUnique();
        });
        builder.Navigation(x => x.Email).IsRequired();

        builder.Property(x => x.PasswordHash)
            .HasConversion(v => v!.Value, v => PasswordHash.Create(v).Value!)
            .IsRequired(false)
            .HasMaxLength(500);

        builder.Property(x => x.UserType)
            .HasConversion(v => v.Name, v => Enumeration.FromName<UserType>(v)!)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.Status)
            .HasConversion(v => v.Name, v => Enumeration.FromName<UserStatus>(v)!)
            .IsRequired()
            .HasMaxLength(50);

        builder.Ignore(x => x.IsActive);
        builder.Property(x => x.InvitationToken).HasMaxLength(200);

        builder.OwnsMany<UserRole>("Roles", ur =>
        {
            ur.ToTable("UserRoles", "identity");
            ur.WithOwner().HasForeignKey(x => x.UserId);
            ur.Property(x => x.UserId)
                .HasConversion(v => v.Value, v => UserId.From(v));
            ur.Property(x => x.RoleId)
                .HasConversion(v => v.Value, v => RoleId.From(v));
            ur.Property(x => x.AssignedAt).IsRequired();
            ur.HasKey("UserId", "RoleId");
        });
    }
}
