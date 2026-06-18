using Contracts.Domain.Aggregates.Contract;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contracts.Infrastructure.Persistence.Configurations;

public sealed class ContractStationConfiguration : IEntityTypeConfiguration<ContractStation>
{
    public void Configure(EntityTypeBuilder<ContractStation> builder)
    {
        builder.ToTable("ContractStations", "contracts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => ContractStationId.From(v))
            .ValueGeneratedNever();

        builder.Property(x => x.ContractId)
            .HasConversion(v => v.Value, v => ContractId.From(v))
            .IsRequired();

        builder.OwnsOne(x => x.Station, s =>
        {
            s.Property(p => p.StationId).HasColumnName("Station_StationId").IsRequired();
            s.Property(p => p.IataCode).HasColumnName("Station_IataCode").HasMaxLength(3).IsRequired();
            s.Property(p => p.Name).HasColumnName("Station_Name").HasMaxLength(200).IsRequired();
        });
        builder.Navigation(x => x.Station).IsRequired();

        builder.HasIndex(x => new { x.ContractId });
    }
}
