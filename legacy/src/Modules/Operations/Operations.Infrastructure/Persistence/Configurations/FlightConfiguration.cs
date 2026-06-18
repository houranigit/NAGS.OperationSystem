using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Operations.Domain.Aggregates.WorkOrder;
using Operations.Domain.Entities;
using Operations.Domain.ValueObjects;
using FlightRoot = Operations.Domain.Aggregates.Flight.Flight;
using FlightId = Operations.Domain.Aggregates.Flight.FlightId;

namespace Operations.Infrastructure.Persistence.Configurations;

public sealed class FlightConfiguration : IEntityTypeConfiguration<FlightRoot>
{
    public void Configure(EntityTypeBuilder<FlightRoot> builder)
    {
        builder.ToTable("Flights", "operations");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => FlightId.From(v));

        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.CanceledAt);
        builder.Property(x => x.ContractId);
        // Snapshot of the contract's human-readable ContractNo at the time the flight was
        // resolved. Same nullability as ContractId. ContractNo VO is at most 30 chars,
        // matching ContractNo.MaxLength on the source aggregate.
        builder.Property(x => x.ContractNumber).HasMaxLength(30);
        builder.Property(x => x.ClientFlightId);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

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

        // Mapped as OwnsOne (same shape as Customer / Station / OperationType above) so
        // the underlying string is reachable from LINQ as f.FlightNumber.Value. With the older
        // HasConversion mapping the VO was a black box and the grid filter expression
        // "(x.FlightNumber ?? \"\").ToLower().Contains(...)" couldn't typecheck because
        // FlightNumber (the VO) is not a string. Storage column is unchanged.
        builder.OwnsOne(x => x.FlightNumber, fn =>
        {
            fn.Property(v => v.Value)
                .HasColumnName("FlightNumber")
                .HasMaxLength(FlightNumber.MaxLength)
                .IsRequired();
        });
        builder.Navigation(x => x.FlightNumber).IsRequired();

        builder.OwnsOne(x => x.Schedule, sch =>
        {
            sch.Property(v => v.Sta).HasColumnName("Sta").IsRequired();
            sch.Property(v => v.Std).HasColumnName("Std").IsRequired();
        });

        builder.OwnsOne(x => x.AircraftType, a =>
        {
            a.Property(v => v.AircraftTypeId).HasColumnName("AircraftTypeId").IsRequired();
            a.Property(v => v.Model).HasColumnName("AircraftTypeModel").HasMaxLength(100).IsRequired();
        }).Navigation(x => x.AircraftType).IsRequired(false);

        builder.OwnsOne(x => x.AcceptedWorkOrder, w =>
        {
            w.Property(v => v.WorkOrderId)
                .HasColumnName("AcceptedWorkOrderId")
                .HasConversion(id => id.Value, v => WorkOrderId.From(v));
            w.Property(v => v.WorkOrderNumber)
                .HasColumnName("AcceptedWorkOrderNumber")
                .HasConversion(v => v.Value, v => WorkOrderNumber.Create(v).Value)
                .HasMaxLength(12);
        });
        builder.Navigation(x => x.AcceptedWorkOrder).IsRequired(false);

        // Child collections: map the public read-only navigations with a backing field, mirroring the Customer
        // reference (Customer.Contacts ↔ _contacts). This lets EF-translated projections use `f.AssignedEmployees`
        // directly inside Select, while mutation still goes through the aggregate's private field.
        builder.Ignore(nameof(FlightRoot.AttachedWorkOrderIds));

        builder.HasMany(x => x.AssignedEmployees)
            .WithOne()
            .HasForeignKey(nameof(FlightAssignedEmployee.FlightId))
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.AssignedEmployees)
            .HasField("_assignedEmployees")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.AttachedWorkOrderLinks)
            .WithOne()
            .HasForeignKey(nameof(FlightWorkOrderAttachment.FlightId))
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.AttachedWorkOrderLinks)
            .HasField("_attachedWorkOrders")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Services)
            .WithOne()
            .HasForeignKey(nameof(FlightService.FlightId))
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Services)
            .HasField("_services")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(nameof(FlightRoot.Status));
        builder.HasIndex(nameof(FlightRoot.CreatedAt));
        builder.HasIndex(nameof(FlightRoot.UpdatedAt));
        builder.HasIndex(nameof(FlightRoot.Status), nameof(FlightRoot.UpdatedAt));
        builder.HasIndex(nameof(FlightRoot.ContractId));

        // Idempotency key for the mobile "ad-hoc from scratch" outbox path. Filtered to
        // non-null rows so contract-bound flights (always null ClientFlightId) don't share
        // the index. Matches the WorkOrder.ClientMutationId pattern: handler does a
        // pre-check, the unique index is the defence-in-depth.
        builder.HasIndex(nameof(FlightRoot.ClientFlightId))
            .IsUnique()
            .HasFilter("[ClientFlightId] IS NOT NULL");
    }
}
