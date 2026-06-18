using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Operations.Domain.Entities;
using FlightId = Operations.Domain.Aggregates.Flight.FlightId;

namespace Operations.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for <see cref="FlightService"/> — the per-flight billable services copied
/// from the contract's OT-services list at creation time.
/// </summary>
public sealed class FlightServiceConfiguration : IEntityTypeConfiguration<FlightService>
{
    public void Configure(EntityTypeBuilder<FlightService> builder)
    {
        builder.ToTable("FlightServices", "operations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.FlightId)
            .HasConversion(v => v.Value, v => FlightId.From(v))
            .IsRequired();

        builder.OwnsOne(x => x.Service, s =>
        {
            s.Property(p => p.ServiceId).HasColumnName("ServiceId").IsRequired();
            s.Property(p => p.Name).HasColumnName("ServiceName").HasMaxLength(150).IsRequired();
            s.Property(p => p.IsAog).HasColumnName("IsAog").IsRequired();
        });
        builder.Navigation(x => x.Service).IsRequired();

        builder.HasIndex(x => x.FlightId);
    }
}
