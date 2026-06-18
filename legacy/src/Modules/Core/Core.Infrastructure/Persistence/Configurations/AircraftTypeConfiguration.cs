using Core.Domain.Aggregates.AircraftType;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.Infrastructure.Persistence.Configurations;

public sealed class AircraftTypeConfiguration : IEntityTypeConfiguration<AircraftType>
{
    public void Configure(EntityTypeBuilder<AircraftType> builder)
    {
        builder.ToTable("AircraftTypes", "core");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => AircraftTypeId.From(v));

        builder.Property(x => x.Manufacturer)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.Model)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.Notes)
            .IsRequired(false)
            .HasMaxLength(500);

        builder.HasIndex(x => new { x.Manufacturer, x.Model }).IsUnique();

        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);

        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.UpdatedAt);
    }
}
