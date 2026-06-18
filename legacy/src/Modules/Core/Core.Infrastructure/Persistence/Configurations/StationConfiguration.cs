using Core.Domain.Aggregates.Country;
using Core.Domain.Aggregates.Station;
using Core.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.Infrastructure.Persistence.Configurations;

public sealed class StationConfiguration : IEntityTypeConfiguration<Station>
{
    public void Configure(EntityTypeBuilder<Station> builder)
    {
        builder.ToTable("Stations", "core");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => StationId.From(v));

        // OwnsOne (not HasConversion) so the underlying string is reachable from LINQ as
        // x.IataCode.Value — matches the FlightConfiguration.FlightNumber pattern. Without this,
        // Radzen DataGrid filter expressions like "(IataCode ?? \"\").ToLower().Contains(...)"
        // can't typecheck because the IataCode VO is opaque to System.Linq.Dynamic.Core.
        builder.OwnsOne(x => x.IataCode, ic =>
        {
            ic.Property(v => v.Value)
                .HasColumnName("IataCode")
                .HasMaxLength(3)
                .IsRequired();

            ic.HasIndex(v => v.Value).IsUnique();
        });
        builder.Navigation(x => x.IataCode).IsRequired();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(x => x.City)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.CountryId)
            .HasConversion(v => v.Value, v => CountryId.From(v))
            .IsRequired();

        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);

        builder.HasIndex(x => x.CountryId);
        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => x.Name);
        builder.HasIndex(x => x.City);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.UpdatedAt);
        // IataCode unique index is declared inside the OwnsOne builder above.
    }
}
