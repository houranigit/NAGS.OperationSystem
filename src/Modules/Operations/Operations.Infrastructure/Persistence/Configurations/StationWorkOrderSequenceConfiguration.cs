using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Operations.Domain.Sequences;

namespace Operations.Infrastructure.Persistence.Configurations;

public sealed class StationWorkOrderSequenceConfiguration : IEntityTypeConfiguration<StationWorkOrderSequence>
{
    public void Configure(EntityTypeBuilder<StationWorkOrderSequence> builder)
    {
        builder.ToTable("station_work_order_sequences");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.StationIata).HasMaxLength(3).IsRequired();
        builder.Property(s => s.LastValue).IsRequired();
    }
}
