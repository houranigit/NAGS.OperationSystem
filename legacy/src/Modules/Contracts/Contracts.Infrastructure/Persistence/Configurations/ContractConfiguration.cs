using Contracts.Domain.Aggregates.Contract;
using Contracts.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ContractAggregate = Contracts.Domain.Aggregates.Contract.Contract;

namespace Contracts.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for the <see cref="ContractAggregate"/> root + its owned VOs:
/// <see cref="ContractNo"/>, <see cref="ContractPeriod"/>, <see cref="FeesAndRates"/>,
/// <see cref="Termination"/>, plus the frozen Customer / Currency snapshots. Every owned
/// VO uses an explicit column-name prefix so the flattened SQL table stays readable. The
/// per-OT advance payments live in a sibling <c>ContractAdvancePayments</c> table — see
/// <see cref="ContractAdvancePaymentConfiguration"/>.
/// </summary>
public sealed class ContractConfiguration : IEntityTypeConfiguration<ContractAggregate>
{
    public void Configure(EntityTypeBuilder<ContractAggregate> builder)
    {
        builder.ToTable("Contracts", "contracts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => ContractId.From(v))
            .ValueGeneratedNever();

        builder.OwnsOne(x => x.ContractNo, no =>
        {
            no.Property(v => v.Value)
                .HasColumnName("ContractNo")
                .HasMaxLength(30)
                .IsRequired();
            no.HasIndex(v => v.Value).IsUnique();
        });
        builder.Navigation(x => x.ContractNo).IsRequired();

        builder.Property(x => x.CustomerId).IsRequired();
        builder.Property(x => x.CurrencyId).IsRequired();

        builder.OwnsOne(x => x.Customer, c =>
        {
            c.Property(p => p.CustomerId).HasColumnName("Customer_CustomerId").IsRequired();
            c.Property(p => p.IataCode).HasColumnName("Customer_IataCode").HasMaxLength(2).IsRequired();
            c.Property(p => p.Name).HasColumnName("Customer_Name").HasMaxLength(200).IsRequired();
        });
        builder.Navigation(x => x.Customer).IsRequired();

        builder.OwnsOne(x => x.Currency, c =>
        {
            c.Property(p => p.CurrencyId).HasColumnName("Currency_CurrencyId").IsRequired();
            c.Property(p => p.Code).HasColumnName("Currency_Code").HasMaxLength(3).IsRequired();
        });
        builder.Navigation(x => x.Currency).IsRequired();

        builder.OwnsOne(x => x.Period, p =>
        {
            p.Property(v => v.StartDate).HasColumnName("Period_StartDate").IsRequired();
            p.Property(v => v.ExpiryDate).HasColumnName("Period_ExpiryDate").IsRequired();
            p.Property(v => v.ExpiryAlertDays).HasColumnName("Period_ExpiryAlertDays").IsRequired();
            p.Property(v => v.ExpiryAlertInterval).HasColumnName("Period_ExpiryAlertInterval");
        });
        builder.Navigation(x => x.Period).IsRequired();

        builder.OwnsOne(x => x.FeesAndRates, fr =>
        {
            fr.OwnsOne(v => v.AdminFee, f => MapFee(f, "Fees_Admin"));
            fr.Navigation(v => v.AdminFee).IsRequired();
            fr.OwnsOne(v => v.DisbursementFee, f => MapFee(f, "Fees_Disbursement"));
            fr.Navigation(v => v.DisbursementFee).IsRequired();
            fr.OwnsOne(v => v.HolidayFee, f => MapFee(f, "Fees_Holiday"));
            fr.Navigation(v => v.HolidayFee).IsRequired();
            fr.OwnsOne(v => v.NightFee, f => MapFee(f, "Fees_Night"));
            fr.Navigation(v => v.NightFee).IsRequired();
            fr.OwnsOne(v => v.ReturnToRampDiscount, f => MapFee(f, "Fees_ReturnToRamp"));
            fr.Navigation(v => v.ReturnToRampDiscount).IsRequired();
            fr.OwnsOne(v => v.OtherDiscount, f => MapFee(f, "Fees_Other"));
            fr.Navigation(v => v.OtherDiscount).IsRequired();
        });
        builder.Navigation(x => x.FeesAndRates).IsRequired();

        builder.OwnsOne(x => x.Termination, t =>
        {
            t.Property(v => v.Reason).HasColumnName("Termination_Reason").HasMaxLength(500);
            t.Property(v => v.AtUtc).HasColumnName("Termination_AtUtc");
            t.Property(v => v.ByUserId).HasColumnName("Termination_ByUserId");
        });

        builder.Property(x => x.PaymentTerms).IsRequired();
        builder.Property(x => x.ApplyVat).IsRequired();
        builder.Property(x => x.DebriefRequired).IsRequired();
        builder.Property(x => x.Attachment).HasColumnType("varbinary(max)");

        builder.Property(x => x.CancellationBasis).IsRequired();
        builder.Property(x => x.CancellationChargeType).IsRequired();
        builder.Property(x => x.DelayBasis).IsRequired();
        builder.Property(x => x.DelayChargeType).IsRequired();
        builder.Property(x => x.DelayType).IsRequired();

        builder.Property(x => x.Status).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedByUserId).IsRequired();
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.UpdatedByUserId);
        builder.Property(x => x.LastExpiringSoonNotificationAt);

        builder.HasMany(x => x.Stations)
            .WithOne()
            .HasForeignKey(x => x.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.OperationTypes)
            .WithOne()
            .HasForeignKey(x => x.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Services)
            .WithOne()
            .HasForeignKey(x => x.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Manpowers)
            .WithOne()
            .HasForeignKey(x => x.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Tools)
            .WithOne()
            .HasForeignKey(x => x.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Materials)
            .WithOne()
            .HasForeignKey(x => x.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.GeneralSupports)
            .WithOne()
            .HasForeignKey(x => x.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.AdvancePayments)
            .WithOne()
            .HasForeignKey(x => x.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.CancellationBrackets)
            .WithOne()
            .HasForeignKey(x => x.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.DelayBrackets)
            .WithOne()
            .HasForeignKey(x => x.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.Status });
        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.CurrencyId);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.UpdatedAt);
    }

    private static void MapFee<T>(OwnedNavigationBuilder<T, Fee> b, string columnPrefix) where T : class
    {
        b.Property(v => v.Type).HasColumnName($"{columnPrefix}_Type").IsRequired();
        b.Property(v => v.Value).HasColumnName($"{columnPrefix}_Value").HasPrecision(18, 4).IsRequired();
    }
}
