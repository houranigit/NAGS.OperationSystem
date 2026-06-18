using Contracts.Domain.Aggregates.Contract;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contracts.Infrastructure.Persistence.Configurations;

public sealed class ContractOperationTypeConfiguration : IEntityTypeConfiguration<ContractOperationType>
{
    public void Configure(EntityTypeBuilder<ContractOperationType> builder)
    {
        builder.ToTable("ContractOperationTypes", "contracts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => ContractOperationTypeId.From(v))
            .ValueGeneratedNever();

        builder.Property(x => x.ContractId)
            .HasConversion(v => v.Value, v => ContractId.From(v))
            .IsRequired();

        builder.OwnsOne(x => x.OperationType, o =>
        {
            o.Property(p => p.OperationTypeId).HasColumnName("OperationType_OperationTypeId").IsRequired();
            o.Property(p => p.Name).HasColumnName("OperationType_Name").HasMaxLength(100).IsRequired();
        });
        builder.Navigation(x => x.OperationType).IsRequired();

        // Owned collection of contract services (per OT). Stored as a child table so
        // EF Core can persist multiple snapshots per OT without needing a json column.
        builder.OwnsMany(x => x.Services, s =>
        {
            s.ToTable("ContractOperationTypeServices", "contracts");
            s.WithOwner().HasForeignKey("ContractOperationTypeId");
            s.Property<Guid>("Id").ValueGeneratedOnAdd();
            s.HasKey("Id");
            s.Property(p => p.ServiceId).HasColumnName("ServiceId").IsRequired();
            s.Property(p => p.Name).HasColumnName("Name").HasMaxLength(100).IsRequired();
            s.Property(p => p.IsAog).HasColumnName("IsAog").IsRequired();
        });

        builder.HasIndex(x => new { x.ContractId });
    }
}
