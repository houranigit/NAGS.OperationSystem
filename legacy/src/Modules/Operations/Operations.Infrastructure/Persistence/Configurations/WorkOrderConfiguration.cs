using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Operations.Domain.Entities;
using Operations.Domain.ValueObjects;
using FlightId = Operations.Domain.Aggregates.Flight.FlightId;
using WorkOrderRoot = Operations.Domain.Aggregates.WorkOrder.WorkOrder;
using WorkOrderId = Operations.Domain.Aggregates.WorkOrder.WorkOrderId;

namespace Operations.Infrastructure.Persistence.Configurations;

public sealed class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrderRoot>
{
    public void Configure(EntityTypeBuilder<WorkOrderRoot> builder)
    {
        builder.ToTable("WorkOrders", "operations");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => WorkOrderId.From(v));

        builder.Property(x => x.FlightId)
            .HasConversion(
                v => v == null ? null : (Guid?)v.Value,
                v => v == null ? null : FlightId.From(v.Value))
            .IsRequired(false);

        builder.Property(x => x.ConfirmedFlightId)
            .HasConversion(
                v => v == null ? null : (Guid?)v.Value,
                v => v == null ? null : FlightId.From(v.Value))
            .IsRequired(false);

        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.IsCanceled).IsRequired();
        builder.Property(x => x.CanceledAt);
        builder.Property(x => x.MarkedForDeletionAt);
        builder.Property(x => x.AircraftTailNumber).HasMaxLength(20);
        builder.Property(x => x.Remarks).HasMaxLength(2000);
        builder.Property(x => x.CreatedByEmployeeId);
        // PNG bytes captured by the mobile app's signature pad. Stored as varbinary(max) —
        // typical signatures hover around 5–20 KB so a generic blob column keeps things simple
        // and the portal can render them as <img src="data:image/png;base64,..."> via the DTO.
        builder.Property(x => x.CustomerSignature)
            .HasColumnName("CustomerSignature")
            .HasColumnType("varbinary(max)");
        builder.Property(x => x.ClientMutationId);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.Property(x => x.WorkOrderNo)
            .HasColumnName("WorkOrderNo")
            .HasConversion(
                v => v != null ? v.Value : null!,
                v => v != null ? WorkOrderNumber.Create(v).Value : null!)
            .HasMaxLength(12);

        builder.OwnsOne(x => x.Customer, c =>
        {
            c.Property(v => v.CustomerId).HasColumnName("CustomerId").IsRequired();
            c.Property(v => v.IataCode).HasColumnName("CustomerIataCode").HasMaxLength(2).IsRequired();
            c.Property(v => v.Name).HasColumnName("CustomerName").HasMaxLength(200).IsRequired();
        });

        builder.OwnsOne(x => x.Station, s =>
        {
            s.Property(v => v.StationId).HasColumnName("StationId").IsRequired();
            s.Property(v => v.Name).HasColumnName("StationName").HasMaxLength(150).IsRequired();
            s.Property(v => v.IataCode).HasColumnName("StationIataCode").HasMaxLength(3).IsRequired();
        });

        builder.OwnsOne(x => x.OperationType, o =>
        {
            o.Property(v => v.OperationTypeId).HasColumnName("OperationTypeId").IsRequired();
            o.Property(v => v.Name).HasColumnName("OperationTypeName").HasMaxLength(150).IsRequired();
        });

        builder.Property(x => x.FlightNumber)
            .HasColumnName("FlightNumber")
            .HasConversion(v => v.Value, v => FlightNumber.Create(v).Value)
            .HasMaxLength(FlightNumber.MaxLength)
            .IsRequired();

        builder.OwnsOne(x => x.AircraftType, a =>
        {
            a.Property(v => v.AircraftTypeId).HasColumnName("AircraftTypeId").IsRequired();
            a.Property(v => v.Model).HasColumnName("AircraftTypeModel").HasMaxLength(100).IsRequired();
        }).Navigation(x => x.AircraftType).IsRequired(false);

        builder.OwnsOne(x => x.Schedule, sch =>
        {
            sch.Property(v => v.Sta).HasColumnName("Sta").IsRequired();
            sch.Property(v => v.Std).HasColumnName("Std").IsRequired();
        });

        builder.OwnsOne(x => x.TimesActual, t =>
        {
            t.Property(v => v.Ata).HasColumnName("Ata").IsRequired();
            t.Property(v => v.Atd).HasColumnName("Atd").IsRequired();
        }).Navigation(x => x.TimesActual).IsRequired(false);

        // Public read-only collection accessors are backed by private fields; ignore the
        // getters so they are not auto-discovered as duplicate navigations.
        builder.Ignore(nameof(WorkOrderRoot.ServiceLines));
        builder.Ignore(nameof(WorkOrderRoot.Tasks));

        builder.HasMany(typeof(WorkOrderServiceLine), "_serviceLines")
            .WithOne()
            .HasForeignKey(nameof(WorkOrderServiceLine.WorkOrderId))
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(typeof(WorkOrderTask), "_tasks")
            .WithOne()
            .HasForeignKey(nameof(WorkOrderTask.WorkOrderId))
            .OnDelete(DeleteBehavior.Cascade);

        // Cross-owned-type indexes (Station/Customer/Sta) need to live inside their OwnsOne builders.
        // Keeping only indexes on the root entity's direct properties for now.
        builder.HasIndex(nameof(WorkOrderRoot.FlightId));
        builder.HasIndex(nameof(WorkOrderRoot.Status));
        builder.HasIndex(nameof(WorkOrderRoot.CreatedAt));
        builder.HasIndex(nameof(WorkOrderRoot.UpdatedAt));
        builder.HasIndex(nameof(WorkOrderRoot.Status), nameof(WorkOrderRoot.UpdatedAt));
        builder.HasIndex(nameof(WorkOrderRoot.FlightId), nameof(WorkOrderRoot.Status));

        // Deletion job filters by Status == Deleting AND MarkedForDeletionAt <= threshold;
        // a composite index keeps the scan tight even when many WOs share the Deleting state.
        builder.HasIndex(nameof(WorkOrderRoot.Status), nameof(WorkOrderRoot.MarkedForDeletionAt));

        // Mobile context query: "my under-review work order on this flight" filters
        // FlightId + CreatedByEmployeeId + Status — a composite index keeps it tight.
        builder.HasIndex(
            nameof(WorkOrderRoot.FlightId),
            nameof(WorkOrderRoot.CreatedByEmployeeId),
            nameof(WorkOrderRoot.Status));

        // Idempotency key for mobile outbox retries. Filtered to non-null rows so historical
        // portal-originated WOs (always null ClientMutationId) don't contend on the index,
        // and so two unrelated null rows never trigger a unique violation. The handler does
        // a pre-check before persisting; the index is the defence-in-depth that prevents two
        // racing retries from sneaking through.
        builder.HasIndex(nameof(WorkOrderRoot.ClientMutationId))
            .IsUnique()
            .HasFilter("[ClientMutationId] IS NOT NULL");
    }
}
