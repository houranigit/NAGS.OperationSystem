using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Store.Domain.Aggregates.GeneralSupport;
using Store.Domain.Aggregates.GeneralSupportPricePlan;

namespace Store.Infrastructure.Persistence.Configurations;

public sealed class GeneralSupportPricePlanConfiguration : IEntityTypeConfiguration<GeneralSupportPricePlan>
{
    public void Configure(EntityTypeBuilder<GeneralSupportPricePlan> builder)
    {
        builder.ToTable("GeneralSupportPricePlans", "store");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => GeneralSupportPricePlanId.From(v))
            .ValueGeneratedNever();

        builder.Property(x => x.GeneralSupportId)
            .HasConversion(v => v.Value, v => GeneralSupportId.From(v))
            .IsRequired();

        builder.Property(x => x.CurrencyId).IsRequired();

        builder.Property(x => x.Basis)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired(false);

        builder.HasIndex(x => x.GeneralSupportId).IsUnique();
        builder.HasIndex(x => x.CurrencyId);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.UpdatedAt);

        builder.OwnsMany(x => x.Brackets, bracket =>
        {
            bracket.ToTable("GeneralSupportPricePlanBrackets", "store");
            bracket.WithOwner().HasForeignKey("GeneralSupportPricePlanId");
            bracket.Property<int>("Id").ValueGeneratedOnAdd();
            bracket.HasKey("Id");

            bracket.Property(b => b.MinMinutes).IsRequired();
            bracket.Property(b => b.MaxMinutes).IsRequired(false);
            bracket.Property(b => b.BlockSize).IsRequired();
            bracket.Property(b => b.Value).HasColumnType("decimal(18,4)").IsRequired();
            bracket.Property(b => b.BillingMode).HasConversion<int>().IsRequired();
        });

        builder.Metadata.FindNavigation(nameof(GeneralSupportPricePlan.Brackets))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
