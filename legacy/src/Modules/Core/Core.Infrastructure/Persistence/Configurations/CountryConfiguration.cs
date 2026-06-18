using Core.Domain.Aggregates.Country;
using Core.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.Infrastructure.Persistence.Configurations;

public sealed class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        builder.ToTable("Countries", "core");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => CountryId.From(v));

        // OwnsOne so the underlying string is reachable from LINQ as x.Code.Value (Radzen
        // grid filters need a string-typed leaf — see StationConfiguration / FlightConfiguration).
        builder.OwnsOne(x => x.Code, code =>
        {
            code.Property(v => v.Value)
                .HasColumnName("Code")
                .HasMaxLength(2)
                .IsRequired();

            code.HasIndex(v => v.Value).IsUnique();
        });
        builder.Navigation(x => x.Code).IsRequired();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.Name);
        builder.HasIndex(x => x.CreatedAt);
    }
}
