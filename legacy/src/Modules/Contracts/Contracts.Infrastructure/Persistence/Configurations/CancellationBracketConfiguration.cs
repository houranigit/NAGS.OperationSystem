using Contracts.Domain.Aggregates.Contract;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contracts.Infrastructure.Persistence.Configurations;

public sealed class CancellationBracketConfiguration : IEntityTypeConfiguration<CancellationBracket>
{
    public void Configure(EntityTypeBuilder<CancellationBracket> builder)
    {
        builder.ToTable("CancellationBrackets", "contracts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => CancellationBracketId.From(v))
            .ValueGeneratedNever();

        builder.Property(x => x.ContractId)
            .HasConversion(v => v.Value, v => ContractId.From(v))
            .IsRequired();

        builder.Property(x => x.MinMinutes).IsRequired();
        builder.Property(x => x.MaxMinutes);
        builder.Property(x => x.Value).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.SortOrder).IsRequired();

        builder.HasIndex(x => new { x.ContractId, x.SortOrder });
    }
}
