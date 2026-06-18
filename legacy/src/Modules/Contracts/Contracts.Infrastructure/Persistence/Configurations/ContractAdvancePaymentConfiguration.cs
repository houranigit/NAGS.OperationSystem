using Contracts.Domain.Aggregates.Contract;
using Contracts.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contracts.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for the per-OT <see cref="ContractAdvancePayment"/> child entity. Mirrors the
/// shape of <see cref="ContractStationConfiguration"/>: strongly-typed id conversion,
/// owned <c>OperationType</c> snapshot for the historical name, and the existing
/// <see cref="ScheduledAdvancedPayment"/> VO mapped as the <c>Payment</c> column block —
/// money sub-VOs use the same column suffixes the legacy single-payment table used so the
/// migration's column rename stays mechanical.
/// </summary>
public sealed class ContractAdvancePaymentConfiguration : IEntityTypeConfiguration<ContractAdvancePayment>
{
    public void Configure(EntityTypeBuilder<ContractAdvancePayment> builder)
    {
        builder.ToTable("ContractAdvancePayments", "contracts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => ContractAdvancePaymentId.From(v))
            .ValueGeneratedNever();

        builder.Property(x => x.ContractId)
            .HasConversion(v => v.Value, v => ContractId.From(v))
            .IsRequired();

        builder.Property(x => x.OperationTypeId).IsRequired();

        builder.OwnsOne(x => x.OperationType, o =>
        {
            o.Property(p => p.OperationTypeId).HasColumnName("OperationType_OperationTypeId").IsRequired();
            o.Property(p => p.Name).HasColumnName("OperationType_Name").HasMaxLength(100).IsRequired();
        });
        builder.Navigation(x => x.OperationType).IsRequired();

        // One advance payment row per (contract, operation type). The aggregate's
        // ValidateAdvancePayments already prevents duplicates in the domain; this index
        // is defence-in-depth so any direct SQL still gets caught.
        builder.HasIndex(x => new { x.ContractId, x.OperationTypeId })
            .IsUnique()
            .HasDatabaseName("UX_ContractAdvancePayments_Contract_OperationType");

        builder.OwnsOne(x => x.Payment, p =>
        {
            p.Property(v => v.FlightsCount).HasColumnName("FlightsCount").IsRequired();

            p.OwnsOne(v => v.FlightCost, m => MapMoney(m, "FlightCost"));
            p.Navigation(v => v.FlightCost).IsRequired();
            p.OwnsOne(v => v.Balance, m => MapMoney(m, "Balance"));
            p.Navigation(v => v.Balance).IsRequired();
            p.OwnsOne(v => v.Deposit, m => MapMoney(m, "Deposit"));
            p.Navigation(v => v.Deposit).IsRequired();
            p.OwnsOne(v => v.RemainingBalance, m => MapMoney(m, "RemainingBalance"));
            p.Navigation(v => v.RemainingBalance).IsRequired();
            p.OwnsOne(v => v.RemainingDeposit, m => MapMoney(m, "RemainingDeposit"));
            p.Navigation(v => v.RemainingDeposit).IsRequired();
        });
        builder.Navigation(x => x.Payment).IsRequired();
    }

    private static void MapMoney<T>(OwnedNavigationBuilder<T, Money> b, string columnPrefix) where T : class
    {
        b.Property(v => v.Amount).HasColumnName($"{columnPrefix}_Amount").HasPrecision(18, 4).IsRequired();
    }
}
