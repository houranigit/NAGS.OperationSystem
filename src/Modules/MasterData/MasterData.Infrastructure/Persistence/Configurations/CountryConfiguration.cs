using MasterData.Domain.Countries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MasterData.Infrastructure.Persistence.Configurations;

public sealed class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        builder.ToTable("countries");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.IsoCode).HasMaxLength(2).IsRequired();
        builder.HasIndex(c => c.IsoCode).IsUnique();

        builder.Property(c => c.IsActive).IsRequired();
        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.UpdatedAtUtc);

        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.Ignore(c => c.DomainEvents);
    }
}
