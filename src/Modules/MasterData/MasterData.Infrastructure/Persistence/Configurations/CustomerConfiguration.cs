using MasterData.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MasterData.Infrastructure.Persistence.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.IataCode).HasMaxLength(2).IsRequired();
        builder.HasIndex(c => c.IataCode).IsUnique();

        builder.Property(c => c.IcaoCode).HasMaxLength(3);
        builder.HasIndex(c => c.IcaoCode).IsUnique().HasFilter("[IcaoCode] IS NOT NULL");

        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();

        builder.Property(c => c.CountryId).IsRequired();
        builder.HasIndex(c => c.CountryId);

        builder.Property(c => c.OfficialEmail).HasMaxLength(256);
        builder.Property(c => c.OfficialPhone).HasMaxLength(30);
        builder.Property(c => c.LogoFileReference).HasMaxLength(512);

        // Map the owned Address to its own table. Keeping it in the customers table (table splitting)
        // together with the rowversion concurrency token makes EF emit two UPDATEs for one row, which
        // spuriously trips optimistic concurrency on any customer change.
        builder.OwnsOne(c => c.Address, address =>
        {
            address.ToTable("customer_addresses");
            address.WithOwner().HasForeignKey("CustomerId");
            address.Property<Guid>("CustomerId");
            address.HasKey("CustomerId");
            address.Property(a => a.Line1).HasColumnName("Line1").HasMaxLength(200).IsRequired();
            address.Property(a => a.Line2).HasColumnName("Line2").HasMaxLength(200);
            address.Property(a => a.City).HasColumnName("City").HasMaxLength(100).IsRequired();
            address.Property(a => a.Region).HasColumnName("Region").HasMaxLength(100);
            address.Property(a => a.PostalCode).HasColumnName("PostalCode").HasMaxLength(20);
        });
        builder.Navigation(c => c.Address).IsRequired();

        builder.Property(c => c.IsActive).IsRequired();
        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.UpdatedAtUtc);

        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasMany(c => c.Contacts)
            .WithOne()
            .HasForeignKey(ct => ct.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Customer.Contacts))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(c => c.DomainEvents);
    }
}

public sealed class CustomerContactConfiguration : IEntityTypeConfiguration<CustomerContact>
{
    public void Configure(EntityTypeBuilder<CustomerContact> builder)
    {
        builder.ToTable("customer_contacts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.CustomerId).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(150).IsRequired();
        builder.Property(c => c.JobTitle).HasMaxLength(100);
        builder.Property(c => c.Email).HasMaxLength(256).IsRequired();
        builder.Property(c => c.Phone).HasMaxLength(30);
        builder.Property(c => c.LinkedUserId);

        // Email is unique within a customer across active contacts.
        builder.HasIndex(c => new { c.CustomerId, c.Email }).IsUnique().HasFilter("[IsActive] = 1");

        builder.Property(c => c.IsActive).IsRequired();
        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.UpdatedAtUtc);
    }
}
