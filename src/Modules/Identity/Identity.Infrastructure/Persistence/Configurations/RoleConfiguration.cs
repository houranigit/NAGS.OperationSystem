using Identity.Domain.Roles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).HasMaxLength(100).IsRequired();
        builder.Property(r => r.NormalizedName).HasMaxLength(100).IsRequired();
        builder.HasIndex(r => r.NormalizedName).IsUnique();

        builder.Property(r => r.Description).HasMaxLength(500);
        builder.Property(r => r.IsSystem).IsRequired();
        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property(r => r.UpdatedAtUtc);

        builder.PrimitiveCollection("_permissions")
            .HasColumnName("Permissions");

        builder.Ignore(r => r.Permissions);
        builder.Ignore(r => r.DomainEvents);
    }
}
