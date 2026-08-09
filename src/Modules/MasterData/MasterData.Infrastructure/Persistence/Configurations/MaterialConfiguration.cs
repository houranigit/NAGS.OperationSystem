using MasterData.Domain.Materials;
using MasterData.Contracts.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MasterData.Infrastructure.Persistence.Configurations;

public sealed class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> builder)
    {
        builder.ToTable("materials", table =>
            table.HasCheckConstraint("CK_materials_CalculationType", "[CalculationType] IN (0, 1)"));
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(m => m.Name).IsUnique();

        builder.Property(m => m.Description).HasMaxLength(500);
        builder.Property(m => m.CalculationType)
            .HasDefaultValue(ResourceCalculationType.Quantity)
            .ValueGeneratedNever()
            .IsRequired();
        builder.Property(m => m.IsActive).IsRequired();
        builder.Property(m => m.CreatedAtUtc).IsRequired();
        builder.Property(m => m.UpdatedAtUtc);
        builder.Property(m => m.RowVersion).IsRowVersion();

        builder.Ignore(m => m.DomainEvents);
    }
}
