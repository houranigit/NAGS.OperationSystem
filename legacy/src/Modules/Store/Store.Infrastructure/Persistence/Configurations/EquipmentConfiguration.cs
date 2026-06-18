using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Store.Domain.Aggregates.Tool;

namespace Store.Infrastructure.Persistence.Configurations;

public sealed class EquipmentConfiguration : IEntityTypeConfiguration<Equipment>
{
    public void Configure(EntityTypeBuilder<Equipment> builder)
    {
        builder.ToTable("Equipments", "store");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => EquipmentId.From(v));

        builder.Property(x => x.ToolId)
            .HasConversion(v => v.Value, v => ToolId.From(v))
            .IsRequired();

        builder.Property(x => x.FactoryId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.SerialId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.CalibrationDate);

        builder.HasIndex(x => new { x.ToolId, x.FactoryId, x.SerialId }).IsUnique();
        builder.HasIndex(x => x.FactoryId);
        builder.HasIndex(x => x.SerialId);
    }
}
