using MasterData.Domain.Tools;
using MasterData.Contracts.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MasterData.Infrastructure.Persistence.Configurations;

public sealed class ToolConfiguration : IEntityTypeConfiguration<Tool>
{
    public void Configure(EntityTypeBuilder<Tool> builder)
    {
        builder.ToTable("tools", table =>
            table.HasCheckConstraint("CK_tools_CalculationType", "[CalculationType] IN (0, 1)"));
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(t => t.Name).IsUnique();

        builder.Property(t => t.Description).HasMaxLength(500);
        builder.Property(t => t.CalculationType)
            .HasDefaultValue(ResourceCalculationType.Duration)
            .ValueGeneratedNever()
            .IsRequired();
        builder.Property(t => t.IsActive).IsRequired();
        builder.Property(t => t.CreatedAtUtc).IsRequired();
        builder.Property(t => t.UpdatedAtUtc);
        builder.Property(t => t.RowVersion).IsRowVersion();

        builder.HasMany(t => t.Equipments)
            .WithOne()
            .HasForeignKey(e => e.ToolId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Tool.Equipments))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(t => t.DomainEvents);
    }
}

public sealed class EquipmentConfiguration : IEntityTypeConfiguration<Equipment>
{
    public void Configure(EntityTypeBuilder<Equipment> builder)
    {
        builder.ToTable("tool_equipments");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.FactoryId).HasMaxLength(100).IsRequired();
        builder.Property(e => e.SerialId).HasMaxLength(100).IsRequired();
        builder.Property(e => e.CalibrationDate);

        builder.HasIndex(e => e.ToolId);
        builder.HasIndex(e => new { e.ToolId, e.FactoryId, e.SerialId }).IsUnique();
    }
}
