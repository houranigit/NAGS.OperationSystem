using MasterData.Domain.AircraftTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MasterData.Infrastructure.Persistence.Configurations;

public sealed class AircraftTypeConfiguration : IEntityTypeConfiguration<AircraftType>
{
    public void Configure(EntityTypeBuilder<AircraftType> builder)
    {
        builder.ToTable("aircraft_types");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Manufacturer).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.Model).HasMaxLength(50).IsRequired();
        builder.HasIndex(a => new { a.Manufacturer, a.Model }).IsUnique();

        builder.Property(a => a.Notes).HasMaxLength(500);
        builder.Property(a => a.IsActive).IsRequired();
        builder.Property(a => a.CreatedAtUtc).IsRequired();
        builder.Property(a => a.UpdatedAtUtc);
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.Ignore(a => a.DomainEvents);
    }
}
