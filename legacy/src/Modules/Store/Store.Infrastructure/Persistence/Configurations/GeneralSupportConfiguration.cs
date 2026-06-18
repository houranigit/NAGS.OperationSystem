using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Store.Domain.Aggregates.GeneralSupport;
using Store.Domain.Aggregates.Unit;

namespace Store.Infrastructure.Persistence.Configurations;

public sealed class GeneralSupportConfiguration : IEntityTypeConfiguration<GeneralSupport>
{
    public void Configure(EntityTypeBuilder<GeneralSupport> builder)
    {
        builder.ToTable("GeneralSupports", "store");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => GeneralSupportId.From(v));

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasIndex(x => x.Name).IsUnique();

        builder.Property(x => x.UnitId)
            .HasConversion(v => v.Value, v => UnitId.From(v))
            .IsRequired();

        builder.Property(x => x.IsDuration).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(500);

        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);

        builder.HasIndex(x => x.UnitId);
        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => x.IsDuration);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.UpdatedAt);
    }
}
