using Contracts.Domain.Aggregates.Contract;
using Contracts.Domain.Aggregates.Contract.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contracts.Infrastructure.Persistence.Configurations;

public sealed class ContractGeneralSupportConfiguration : IEntityTypeConfiguration<ContractGeneralSupport>
{
    public void Configure(EntityTypeBuilder<ContractGeneralSupport> builder)
    {
        builder.ToTable("ContractGeneralSupports", "contracts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => ContractGeneralSupportId.From(v))
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

        builder.OwnsOne(x => x.GeneralSupport, g =>
        {
            g.Property(p => p.GeneralSupportId).HasColumnName("GeneralSupport_GeneralSupportId").IsRequired();
            g.Property(p => p.Name).HasColumnName("GeneralSupport_Name").HasMaxLength(100).IsRequired();
        });
        builder.Navigation(x => x.GeneralSupport).IsRequired();

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
            b.ToTable("ContractGeneralSupportBrackets", "contracts");
            b.WithOwner().HasForeignKey("ContractGeneralSupportId");
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
