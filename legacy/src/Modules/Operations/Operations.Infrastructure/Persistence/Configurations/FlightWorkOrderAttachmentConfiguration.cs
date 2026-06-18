using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Operations.Domain.Entities;
using FlightId = Operations.Domain.Aggregates.Flight.FlightId;
using WorkOrderId = Operations.Domain.Aggregates.WorkOrder.WorkOrderId;

namespace Operations.Infrastructure.Persistence.Configurations;

public sealed class FlightWorkOrderAttachmentConfiguration : IEntityTypeConfiguration<FlightWorkOrderAttachment>
{
    public void Configure(EntityTypeBuilder<FlightWorkOrderAttachment> builder)
    {
        builder.ToTable("FlightWorkOrderAttachments", "operations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.FlightId)
            .HasConversion(v => v.Value, v => FlightId.From(v))
            .IsRequired();

        builder.Property(x => x.WorkOrderId)
            .HasConversion(v => v.Value, v => WorkOrderId.From(v))
            .IsRequired();

        builder.HasIndex(x => x.FlightId);
        builder.HasIndex(x => x.WorkOrderId);
        builder.HasIndex(nameof(FlightWorkOrderAttachment.FlightId), nameof(FlightWorkOrderAttachment.WorkOrderId)).IsUnique();
    }
}
