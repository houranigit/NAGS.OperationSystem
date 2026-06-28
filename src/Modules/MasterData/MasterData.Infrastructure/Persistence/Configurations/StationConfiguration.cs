using MasterData.Domain.Stations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MasterData.Infrastructure.Persistence.Configurations;

public sealed class StationConfiguration : IEntityTypeConfiguration<Station>
{
    public void Configure(EntityTypeBuilder<Station> builder)
    {
        builder.ToTable("stations");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.IataCode).HasMaxLength(3).IsRequired();
        builder.HasIndex(s => s.IataCode).IsUnique();

        builder.Property(s => s.IcaoCode).HasMaxLength(4);
        // Unique only across the rows that actually have an ICAO code.
        builder.HasIndex(s => s.IcaoCode).IsUnique().HasFilter("[IcaoCode] IS NOT NULL");

        builder.Property(s => s.Name).HasMaxLength(150).IsRequired();
        builder.Property(s => s.City).HasMaxLength(100);

        builder.Property(s => s.CountryId).IsRequired();
        builder.HasIndex(s => s.CountryId);

        builder.Property(s => s.IsActive).IsRequired();
        builder.Property(s => s.CreatedAtUtc).IsRequired();
        builder.Property(s => s.UpdatedAtUtc);

        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.Ignore(s => s.DomainEvents);
    }
}
