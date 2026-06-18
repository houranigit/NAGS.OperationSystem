using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Store.Domain.Aggregates.Material;
using Store.Domain.Aggregates.MaterialPricePlan;

namespace Store.Infrastructure.Persistence.Configurations;

public sealed class MaterialPricePlanConfiguration : IEntityTypeConfiguration<MaterialPricePlan>
{
    public void Configure(EntityTypeBuilder<MaterialPricePlan> builder)
    {
        builder.ToTable("MaterialPricePlans", "store");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => MaterialPricePlanId.From(v))
            .ValueGeneratedNever();

        builder.Property(x => x.MaterialId)
            .HasConversion(v => v.Value, v => MaterialId.From(v))
            .IsRequired();

        builder.Property(x => x.CurrencyId).IsRequired();

        builder.Property(x => x.Basis)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired(false);

        builder.HasIndex(x => x.MaterialId).IsUnique();
        builder.HasIndex(x => x.CurrencyId);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.UpdatedAt);

        builder.OwnsMany(x => x.Brackets, bracket =>
        {
            bracket.ToTable("MaterialPricePlanBrackets", "store");
            bracket.WithOwner().HasForeignKey("MaterialPricePlanId");
            bracket.Property<int>("Id").ValueGeneratedOnAdd();
            bracket.HasKey("Id");

            bracket.Property(b => b.MinMinutes).IsRequired();
            bracket.Property(b => b.MaxMinutes).IsRequired(false);
            bracket.Property(b => b.BlockSize).IsRequired();
            bracket.Property(b => b.Value).HasColumnType("decimal(18,4)").IsRequired();
            bracket.Property(b => b.BillingMode).HasConversion<int>().IsRequired();
        });

        builder.Metadata.FindNavigation(nameof(MaterialPricePlan.Brackets))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
