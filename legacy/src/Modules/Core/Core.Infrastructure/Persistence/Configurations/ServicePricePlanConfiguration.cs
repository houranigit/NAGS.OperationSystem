using Core.Domain.Aggregates.AircraftType;
using Core.Domain.Aggregates.Currency;
using Core.Domain.Aggregates.OperationType;
using Core.Domain.Aggregates.Service;
using Core.Domain.Aggregates.ServicePricePlan;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.Infrastructure.Persistence.Configurations;

public sealed class ServicePricePlanConfiguration : IEntityTypeConfiguration<ServicePricePlan>
{
    public void Configure(EntityTypeBuilder<ServicePricePlan> builder)
    {
        builder.ToTable("ServicePricePlans", "core");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => ServicePricePlanId.From(v))
            .ValueGeneratedNever();

        builder.Property(x => x.ServiceId)
            .HasConversion(v => v.Value, v => ServiceId.From(v))
            .IsRequired();

        builder.Property(x => x.OperationTypeId)
            .HasConversion(v => v.Value, v => OperationTypeId.From(v))
            .IsRequired();

        builder.Property(x => x.AircraftTypeId)
            .HasConversion(
                v => v != null ? v.Value : (Guid?)null,
                v => v.HasValue ? AircraftTypeId.From(v.Value) : null)
            .IsRequired(false);

        builder.Property(x => x.CurrencyId)
            .HasConversion(v => v.Value, v => CurrencyId.From(v))
            .IsRequired();

        builder.Property(x => x.Basis)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired(false);

        builder.HasIndex(x => new { x.ServiceId, x.OperationTypeId })
            .IsUnique()
            .HasFilter("[AircraftTypeId] IS NULL")
            .HasDatabaseName("IX_ServicePricePlans_Service_Operation_NullAircraft");

        builder.HasIndex(x => new { x.ServiceId, x.OperationTypeId, x.AircraftTypeId })
            .IsUnique()
            .HasFilter("[AircraftTypeId] IS NOT NULL")
            .HasDatabaseName("IX_ServicePricePlans_Service_Operation_Aircraft");

        builder.HasIndex(x => x.CurrencyId);
        builder.HasIndex(x => x.AircraftTypeId);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.UpdatedAt);

        builder.OwnsMany(x => x.Brackets, bracket =>
        {
            bracket.ToTable("ServicePricePlanBrackets", "core");
            bracket.WithOwner().HasForeignKey("ServicePricePlanId");
            bracket.Property<int>("Id").ValueGeneratedOnAdd();
            bracket.HasKey("Id");

            bracket.Property(b => b.MinMinutes).IsRequired();
            bracket.Property(b => b.MaxMinutes).IsRequired(false);
            bracket.Property(b => b.BlockSize).IsRequired();
            bracket.Property(b => b.Value).HasColumnType("decimal(18,4)").IsRequired();
            bracket.Property(b => b.BillingMode).HasConversion<int>().IsRequired();
        });

        builder.Metadata.FindNavigation(nameof(ServicePricePlan.Brackets))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
