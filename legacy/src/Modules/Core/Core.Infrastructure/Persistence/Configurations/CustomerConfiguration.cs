using Core.Domain.Aggregates.Country;
using Core.Domain.Aggregates.Customer;
using Core.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.Infrastructure.Persistence.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers", "core");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => CustomerId.From(v));

        // OwnsOne so the underlying string is reachable from LINQ as x.IataCode.Value (see
        // StationConfiguration / FlightConfiguration for the same pattern).
        builder.OwnsOne(x => x.IataCode, ic =>
        {
            ic.Property(v => v.Value)
                .HasColumnName("IataCode")
                .HasMaxLength(2)
                .IsRequired();

            ic.HasIndex(v => v.Value).IsUnique();
        });
        builder.Navigation(x => x.IataCode).IsRequired();

        builder.Property(x => x.IcaoCode)
            .IsRequired(false)
            .HasMaxLength(3);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.OfficialEmail)
            .IsRequired(false)
            .HasMaxLength(254);

        builder.Property(x => x.OfficialPhone)
            .IsRequired(false)
            .HasMaxLength(50);

        builder.Property(x => x.Logo)
            .IsRequired(false)
            .HasColumnType("varbinary(max)");

        builder.OwnsOne(x => x.Address, address =>
        {
            address.Property(a => a.Line1)
                .HasColumnName("Address_Line1")
                .HasMaxLength(200)
                .IsRequired(false);

            address.Property(a => a.Line2)
                .HasColumnName("Address_Line2")
                .HasMaxLength(200)
                .IsRequired(false);

            address.Property(a => a.City)
                .HasColumnName("Address_City")
                .HasMaxLength(100)
                .IsRequired(false);

            address.Property(a => a.PostalCode)
                .HasColumnName("Address_PostalCode")
                .HasMaxLength(20)
                .IsRequired(false);

            address.Property(a => a.CountryId)
                .HasColumnName("Address_CountryId")
                .HasConversion(v => v.Value, v => CountryId.From(v))
                .IsRequired(false);

            address.HasOne(typeof(Country), nameof(Address.Country))
                .WithMany()
                .HasForeignKey(nameof(Address.CountryId))
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.HasMany(x => x.Contacts)
            .WithOne()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);

        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => x.Name);
        builder.HasIndex(x => x.IcaoCode);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.UpdatedAt);
    }
}
