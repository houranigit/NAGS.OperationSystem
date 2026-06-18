using Identity.Domain.Aggregates.Role;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles", "identity");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => RoleId.From(v));

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(x => x.Name).IsUnique();

        builder.Property(x => x.Description).HasMaxLength(500);

        builder.OwnsMany<RolePermission>("Permissions", perm =>
        {
            perm.ToTable("RolePermissions", "identity");
            perm.WithOwner().HasForeignKey(x => x.RoleId);
            perm.Property(x => x.RoleId)
                .HasConversion(v => v.Value, v => RoleId.From(v));
            perm.Property(x => x.Permission).IsRequired().HasMaxLength(200);
            perm.HasKey("RoleId", "Permission");
        });
    }
}
