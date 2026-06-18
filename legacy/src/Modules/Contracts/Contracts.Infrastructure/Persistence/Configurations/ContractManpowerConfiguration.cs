using Contracts.Domain.Aggregates.Contract;
using Contracts.Domain.Aggregates.Contract.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contracts.Infrastructure.Persistence.Configurations;

public sealed class ContractManpowerConfiguration : IEntityTypeConfiguration<ContractManpower>
{
    public void Configure(EntityTypeBuilder<ContractManpower> builder)
    {
        builder.ToTable("ContractManpowers", "contracts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => ContractManpowerId.From(v))
            .ValueGeneratedNever();

        builder.Property(x => x.ContractId)
            .HasConversion(v => v.Value, v => ContractId.From(v))
            .IsRequired();

        builder.Property(x => x.OperationTypeId).IsRequired();
        builder.Property(x => x.Basis).IsRequired();

        builder.OwnsOne(x => x.OperationType, o =>
        {
            o.Property(p => p.OperationTypeId).HasColumnName("OperationType_OperationTypeId").IsRequired();
            o.Property(p => p.Name).HasColumnName("OperationType_Name").HasMaxLength(100).IsRequired();
        });
        builder.Navigation(x => x.OperationType).IsRequired();

        builder.OwnsOne(x => x.ManpowerType, m =>
        {
            m.Property(p => p.ManpowerTypeId).HasColumnName("ManpowerType_ManpowerTypeId").IsRequired();
            m.Property(p => p.Name).HasColumnName("ManpowerType_Name").HasMaxLength(100).IsRequired();
        });
        builder.Navigation(x => x.ManpowerType).IsRequired();

        builder.OwnsOne(x => x.PackagePaidBalance, m =>
        {
            m.Property(v => v.Amount).HasColumnName("PackagePaidBalance_Amount").HasPrecision(18, 4);
        });
        builder.OwnsOne(x => x.PackageRemainingBalance, m =>
        {
            m.Property(v => v.Amount).HasColumnName("PackageRemainingBalance_Amount").HasPrecision(18, 4);
        });

        builder.OwnsMany(x => x.Brackets, b =>
        {
            b.ToTable("ContractManpowerBrackets", "contracts");
            b.WithOwner().HasForeignKey("ContractManpowerId");
            b.Property<int>("Id").ValueGeneratedOnAdd();
            b.HasKey("Id");

            b.Property(p => p.MinMinutes).IsRequired();
            b.Property(p => p.MaxMinutes);
            b.Property(p => p.BlockSize).IsRequired();
            b.Property(p => p.PriceValue).HasPrecision(18, 4).IsRequired();
            b.Property(p => p.PackagePriceValue).HasPrecision(18, 4);
            b.Property(p => p.BillingMode).IsRequired();
        });

        builder.HasIndex(x => new { x.ContractId, x.OperationTypeId });
    }
}
