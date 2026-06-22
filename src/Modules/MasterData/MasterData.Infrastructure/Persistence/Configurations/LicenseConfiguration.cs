using MasterData.Domain.Licenses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MasterData.Infrastructure.Persistence.Configurations;

public sealed class LicenseConfiguration : IEntityTypeConfiguration<License>
{
    public void Configure(EntityTypeBuilder<License> builder)
    {
        builder.ToTable("licenses");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Code).HasMaxLength(10).IsRequired();
        builder.HasIndex(l => l.Code).IsUnique();

        builder.Property(l => l.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(l => l.Name);

        builder.Property(l => l.Description).HasMaxLength(500);

        builder.Property(l => l.IsActive).IsRequired();
        builder.Property(l => l.CreatedAtUtc).IsRequired();
        builder.Property(l => l.UpdatedAtUtc);

        builder.Property(l => l.RowVersion).IsRowVersion();

        builder.Ignore(l => l.DomainEvents);
    }
}
