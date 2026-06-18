using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Operations.Infrastructure.Persistence.Configurations;

public sealed class StationWorkOrderCounterConfiguration : IEntityTypeConfiguration<StationWorkOrderCounter>
{
    public void Configure(EntityTypeBuilder<StationWorkOrderCounter> builder)
    {
        builder.ToTable("StationWorkOrderCounters", "operations");
        builder.HasKey(x => x.StationId);
        builder.Property(x => x.LastSequence).IsRequired();
    }
}
