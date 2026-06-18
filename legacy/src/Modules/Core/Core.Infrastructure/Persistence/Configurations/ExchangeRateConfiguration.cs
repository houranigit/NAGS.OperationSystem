using Core.Domain.Aggregates.Currency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.Infrastructure.Persistence.Configurations;

public sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.ToTable("ExchangeRates", "core");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => ExchangeRateId.From(v));

        builder.Property(x => x.CurrencyId)
            .HasConversion(v => v.Value, v => CurrencyId.From(v))
            .IsRequired();

        builder.Property(x => x.ToCurrencyId)
            .HasConversion(v => v.Value, v => CurrencyId.From(v))
            .IsRequired();

        builder.Property(x => x.Rate)
            .HasPrecision(18, 6)
            .IsRequired();

        builder.Property(x => x.CreatedById).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);

        builder.HasIndex(x => new { x.CurrencyId, x.ToCurrencyId });
        builder.HasIndex(x => x.ToCurrencyId);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.UpdatedAt);

        builder.HasOne(x => x.TargetCurrency)
            .WithMany()
            .HasForeignKey(x => x.ToCurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
