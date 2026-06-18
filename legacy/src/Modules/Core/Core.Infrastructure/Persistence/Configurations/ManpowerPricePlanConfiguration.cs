using Core.Domain.Aggregates.Currency;
using Core.Domain.Aggregates.ManpowerPricePlan;
using Core.Domain.Aggregates.ManpowerType;
using Core.Domain.Aggregates.OperationType;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.Infrastructure.Persistence.Configurations;

public sealed class ManpowerPricePlanConfiguration : IEntityTypeConfiguration<ManpowerPricePlan>
{
    public void Configure(EntityTypeBuilder<ManpowerPricePlan> builder)
    {
        builder.ToTable("ManpowerPricePlans", "core");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => ManpowerPricePlanId.From(v))
            .ValueGeneratedNever();

        builder.Property(x => x.ManpowerTypeId)
            .HasConversion(v => v.Value, v => ManpowerTypeId.From(v))
            .IsRequired();

        builder.Property(x => x.OperationTypeId)
            .HasConversion(v => v.Value, v => OperationTypeId.From(v))
            .IsRequired();

        builder.Property(x => x.CurrencyId)
            .HasConversion(v => v.Value, v => CurrencyId.From(v))
            .IsRequired();

        builder.Property(x => x.Basis)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired(false);

        builder.HasIndex(x => new { x.ManpowerTypeId, x.OperationTypeId }).IsUnique();
        builder.HasIndex(x => x.CurrencyId);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.UpdatedAt);

        builder.OwnsMany(x => x.Brackets, bracket =>
        {
            bracket.ToTable("ManpowerPricePlanBrackets", "core");
            bracket.WithOwner().HasForeignKey("ManpowerPricePlanId");
            bracket.Property<int>("Id").ValueGeneratedOnAdd();
            bracket.HasKey("Id");

            bracket.Property(b => b.MinMinutes).IsRequired();
            bracket.Property(b => b.MaxMinutes).IsRequired(false);
            bracket.Property(b => b.BlockSize).IsRequired();
            bracket.Property(b => b.Value).HasColumnType("decimal(18,4)").IsRequired();
            bracket.Property(b => b.BillingMode).HasConversion<int>().IsRequired();
        });

        builder.Metadata.FindNavigation(nameof(ManpowerPricePlan.Brackets))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
