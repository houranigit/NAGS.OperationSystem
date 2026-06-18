using Core.Domain.Aggregates.Currency;
using Core.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.Infrastructure.Persistence.Configurations;

public sealed class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("Currencies", "core");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => CurrencyId.From(v));

        // OwnsOne so the underlying string is reachable from LINQ as x.Code.Value (see
        // StationConfiguration / FlightConfiguration for the same pattern).
        builder.OwnsOne(x => x.Code, code =>
        {
            code.Property(v => v.Value)
                .HasColumnName("Code")
                .HasMaxLength(3)
                .IsRequired();

            code.HasIndex(v => v.Value).IsUnique();
        });
        builder.Navigation(x => x.Code).IsRequired();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasMany(x => x.ExchangeRates)
            .WithOne()
            .HasForeignKey(x => x.CurrencyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);

        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => x.Name);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.UpdatedAt);
    }
}
