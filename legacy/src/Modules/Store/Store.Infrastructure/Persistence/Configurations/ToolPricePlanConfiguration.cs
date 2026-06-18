using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Store.Domain.Aggregates.Tool;
using Store.Domain.Aggregates.ToolPricePlan;

namespace Store.Infrastructure.Persistence.Configurations;

public sealed class ToolPricePlanConfiguration : IEntityTypeConfiguration<ToolPricePlan>
{
    public void Configure(EntityTypeBuilder<ToolPricePlan> builder)
    {
        builder.ToTable("ToolPricePlans", "store");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => ToolPricePlanId.From(v))
            .ValueGeneratedNever();

        builder.Property(x => x.ToolId)
            .HasConversion(v => v.Value, v => ToolId.From(v))
            .IsRequired();

        builder.Property(x => x.CurrencyId).IsRequired();

        builder.Property(x => x.Basis)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired(false);

        builder.HasIndex(x => x.ToolId).IsUnique();
        builder.HasIndex(x => x.CurrencyId);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.UpdatedAt);

        builder.OwnsMany(x => x.Brackets, bracket =>
        {
            bracket.ToTable("ToolPricePlanBrackets", "store");
            bracket.WithOwner().HasForeignKey("ToolPricePlanId");
            bracket.Property<int>("Id").ValueGeneratedOnAdd();
            bracket.HasKey("Id");

            bracket.Property(b => b.MinMinutes).IsRequired();
            bracket.Property(b => b.MaxMinutes).IsRequired(false);
            bracket.Property(b => b.BlockSize).IsRequired();
            bracket.Property(b => b.Value).HasColumnType("decimal(18,4)").IsRequired();
            bracket.Property(b => b.BillingMode).HasConversion<int>().IsRequired();
        });

        builder.Metadata.FindNavigation(nameof(ToolPricePlan.Brackets))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
