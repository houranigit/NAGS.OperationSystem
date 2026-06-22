using MasterData.Domain.ManpowerTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MasterData.Infrastructure.Persistence.Configurations;

public sealed class ManpowerTypeConfiguration : IEntityTypeConfiguration<ManpowerType>
{
    public void Configure(EntityTypeBuilder<ManpowerType> builder)
    {
        builder.ToTable("manpower_types");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(m => m.Name).IsUnique();

        builder.Property(m => m.Description).HasMaxLength(500);

        builder.Property(m => m.IsActive).IsRequired();
        builder.Property(m => m.CreatedAtUtc).IsRequired();
        builder.Property(m => m.UpdatedAtUtc);

        builder.Property(m => m.RowVersion).IsRowVersion();

        builder.Ignore(m => m.DomainEvents);
    }
}
