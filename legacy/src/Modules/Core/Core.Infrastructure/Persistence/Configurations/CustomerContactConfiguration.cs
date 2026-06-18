using Core.Domain.Aggregates.Customer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.Infrastructure.Persistence.Configurations;

public sealed class CustomerContactConfiguration : IEntityTypeConfiguration<CustomerContact>
{
    public void Configure(EntityTypeBuilder<CustomerContact> builder)
    {
        builder.ToTable("CustomerContacts", "core");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => CustomerContactId.From(v));

        builder.Property(x => x.CustomerId)
            .HasConversion(v => v.Value, v => CustomerId.From(v))
            .IsRequired();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(x => x.JobTitle)
            .IsRequired(false)
            .HasMaxLength(100);

        builder.Property(x => x.Email)
            .IsRequired()
            .HasMaxLength(254);

        builder.Property(x => x.Phone)
            .IsRequired(false)
            .HasMaxLength(50);

        builder.Property(x => x.LinkedUserId)
            .IsRequired(false);

        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => new { x.CustomerId, x.Email }).IsUnique();
        builder.HasIndex(x => x.Email);
        builder.HasIndex(x => x.Name);
        builder.HasIndex(x => x.CreatedAt);
    }
}
