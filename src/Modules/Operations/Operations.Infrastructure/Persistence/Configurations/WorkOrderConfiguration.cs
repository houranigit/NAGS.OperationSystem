using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Operations.Domain.Flights;
using Operations.Domain.WorkOrders;

namespace Operations.Infrastructure.Persistence.Configurations;

public sealed class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    public void Configure(EntityTypeBuilder<WorkOrder> builder)
    {
        builder.ToTable("work_orders");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();
        builder.Property(w => w.FlightId).IsRequired();
        builder.Property(w => w.Type).HasConversion<int>();
        builder.Property(w => w.Status).HasConversion<int>();
        builder.Property(w => w.OwnerUserId).IsRequired();
        builder.Property(w => w.IsMergeGenerated).IsRequired();
        builder.Property(w => w.AircraftTailNumber).HasMaxLength(20);
        builder.Property(w => w.Remarks).HasMaxLength(2000);
        builder.Property(w => w.CustomerSignatureReference).HasMaxLength(500);
        builder.Property(w => w.CustomerSignatureFileName).HasMaxLength(255);
        builder.Property(w => w.CustomerSignatureContentType).HasMaxLength(100);
        builder.Property(w => w.ApprovalNumber).HasMaxLength(20);
        builder.Property(w => w.CreatedAtUtc).IsRequired();
        builder.Property(w => w.RowVersion).IsRowVersion();

        builder.OwnsOne(w => w.Owner, o =>
        {
            o.Property(p => p.StaffMemberId).HasColumnName("OwnerStaffMemberId");
            o.Property(p => p.FullName).HasColumnName("OwnerFullName").HasMaxLength(200);
            o.Property(p => p.EmployeeId).HasColumnName("OwnerEmployeeId").HasMaxLength(50);
        });

        builder.OwnsOne(w => w.Customer, c =>
        {
            c.Property(p => p.CustomerId).HasColumnName("CustomerId").IsRequired();
            c.Property(p => p.IataCode).HasColumnName("CustomerIataCode").HasMaxLength(3);
            c.Property(p => p.Name).HasColumnName("CustomerName").HasMaxLength(200).IsRequired();
        });

        builder.OwnsOne(w => w.Station, s =>
        {
            s.Property(p => p.StationId).HasColumnName("StationId").IsRequired();
            s.Property(p => p.IataCode).HasColumnName("StationIataCode").HasMaxLength(3).IsRequired();
            s.Property(p => p.Name).HasColumnName("StationName").HasMaxLength(150).IsRequired();
        });

        builder.OwnsOne(w => w.OperationType, o =>
        {
            o.Property(p => p.OperationTypeId).HasColumnName("OperationTypeId").IsRequired();
            o.Property(p => p.Name).HasColumnName("OperationTypeName").HasMaxLength(100).IsRequired();
        });

        builder.OwnsOne(w => w.PlannedFlightNumber, n =>
            n.Property(p => p.Value).HasColumnName("PlannedFlightNumber").HasMaxLength(12).IsRequired());

        builder.OwnsOne(w => w.ActualFlightNumber, n =>
            n.Property(p => p.Value).HasColumnName("ActualFlightNumber").HasMaxLength(12).IsRequired());

        builder.OwnsOne(w => w.Schedule, s =>
        {
            s.Property(p => p.Sta).HasColumnName("ScheduledArrivalUtc").IsRequired();
            s.Property(p => p.Std).HasColumnName("ScheduledDepartureUtc").IsRequired();
        });

        builder.OwnsOne(w => w.AircraftType, a =>
        {
            a.Property(p => p.AircraftTypeId).HasColumnName("AircraftTypeId");
            a.Property(p => p.Manufacturer).HasColumnName("AircraftManufacturer").HasMaxLength(100);
            a.Property(p => p.Model).HasColumnName("AircraftModel").HasMaxLength(50);
        });

        builder.OwnsOne(w => w.Actuals, a =>
        {
            a.Property(p => p.Ata).HasColumnName("ActualArrivalUtc");
            a.Property(p => p.Atd).HasColumnName("ActualDepartureUtc");
        });

        builder.OwnsOne(w => w.Cancellation, c =>
        {
            c.Property(p => p.CanceledAtUtc).HasColumnName("CanceledAtUtc");
            c.Property(p => p.Reason).HasColumnName("CancellationReason").HasMaxLength(1000);
        });

        builder.HasMany(w => w.ServiceLines).WithOne().HasForeignKey(l => l.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(w => w.Tasks).WithOne().HasForeignKey(t => t.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Flight>().WithMany().HasForeignKey(w => w.FlightId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(w => w.FlightId);
        builder.HasIndex(w => w.Status);
        builder.HasIndex(w => w.OwnerUserId);
        builder.HasIndex(w => new { w.FlightId, w.OwnerUserId })
            .IsUnique()
            .HasFilter("[Status] IN (0, 1, 2) AND [IsMergeGenerated] = CAST(0 AS bit)");
        builder.HasIndex(w => w.FlightId)
            .IsUnique()
            .HasFilter("[Status] = 2");

        builder.Ignore(w => w.IsEditable);
        builder.Ignore(w => w.DomainEvents);
    }
}

public sealed class WorkOrderServiceLineConfiguration : IEntityTypeConfiguration<WorkOrderServiceLine>
{
    public void Configure(EntityTypeBuilder<WorkOrderServiceLine> builder)
    {
        builder.ToTable("work_order_service_lines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.WorkOrderId).IsRequired();
        builder.Property(l => l.Description).HasMaxLength(2000);
        builder.Property(l => l.IsReturnToRamp).IsRequired();

        builder.OwnsOne(l => l.Service, s =>
        {
            s.Property(p => p.ServiceId).HasColumnName("ServiceId").IsRequired();
            s.Property(p => p.Name).HasColumnName("ServiceName").HasMaxLength(200).IsRequired();
        });

        builder.OwnsOne(l => l.PerformedBy, s =>
        {
            s.Property(p => p.StaffMemberId).HasColumnName("PerformedByStaffMemberId").IsRequired();
            s.Property(p => p.FullName).HasColumnName("PerformedByFullName").HasMaxLength(200).IsRequired();
            s.Property(p => p.EmployeeId).HasColumnName("PerformedByEmployeeId").HasMaxLength(50).IsRequired();
        });

        builder.OwnsOne(l => l.Window, w =>
        {
            w.Property(p => p.From).HasColumnName("FromUtc").IsRequired();
            w.Property(p => p.To).HasColumnName("ToUtc").IsRequired();
        });

        builder.HasIndex(l => l.WorkOrderId);
        builder.Ignore(l => l.IsAircraftPerLanding);
    }
}

public sealed class WorkOrderTaskConfiguration : IEntityTypeConfiguration<WorkOrderTask>
{
    public void Configure(EntityTypeBuilder<WorkOrderTask> builder)
    {
        builder.ToTable("work_order_tasks");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.WorkOrderId).IsRequired();
        builder.Property(t => t.TaskType).HasConversion<int>();
        builder.Property(t => t.Description).HasMaxLength(2000);
        builder.Property(t => t.IsReturnToRamp).IsRequired();

        builder.OwnsOne(t => t.Window, w =>
        {
            w.Property(p => p.From).HasColumnName("FromUtc").IsRequired();
            w.Property(p => p.To).HasColumnName("ToUtc").IsRequired();
        });

        builder.HasMany(t => t.Employees).WithOne().HasForeignKey(e => e.WorkOrderTaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.Tools).WithOne().HasForeignKey(e => e.WorkOrderTaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.Materials).WithOne().HasForeignKey(e => e.WorkOrderTaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.GeneralSupports).WithOne().HasForeignKey(e => e.WorkOrderTaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.Attachments).WithOne().HasForeignKey(e => e.WorkOrderTaskId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.WorkOrderId);
    }
}

public sealed class WorkOrderTaskEmployeeConfiguration : IEntityTypeConfiguration<WorkOrderTaskEmployee>
{
    public void Configure(EntityTypeBuilder<WorkOrderTaskEmployee> builder)
    {
        builder.ToTable("work_order_task_employees");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.WorkOrderId).IsRequired();
        builder.Property(e => e.WorkOrderTaskId).IsRequired();

        builder.OwnsOne(e => e.Employee, s =>
        {
            s.Property(p => p.StaffMemberId).HasColumnName("StaffMemberId").IsRequired();
            s.Property(p => p.FullName).HasColumnName("StaffFullName").HasMaxLength(200).IsRequired();
            s.Property(p => p.EmployeeId).HasColumnName("StaffEmployeeId").HasMaxLength(50).IsRequired();
        });

        builder.HasIndex(e => e.WorkOrderTaskId);
    }
}

public sealed class WorkOrderTaskToolConfiguration : IEntityTypeConfiguration<WorkOrderTaskTool>
{
    public void Configure(EntityTypeBuilder<WorkOrderTaskTool> builder)
    {
        builder.ToTable("work_order_task_tools");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.WorkOrderId).IsRequired();
        builder.Property(t => t.WorkOrderTaskId).IsRequired();

        builder.OwnsOne(t => t.Tool, tool =>
        {
            tool.Property(p => p.ToolId).HasColumnName("ToolId").IsRequired();
            tool.Property(p => p.Name).HasColumnName("ToolName").HasMaxLength(200).IsRequired();
        });

        builder.OwnsOne(t => t.Quantity, q =>
            q.Property(p => p.Value).HasColumnName("Quantity").HasPrecision(18, 2).IsRequired());

        builder.HasIndex(t => t.WorkOrderTaskId);
    }
}

public sealed class WorkOrderTaskMaterialConfiguration : IEntityTypeConfiguration<WorkOrderTaskMaterial>
{
    public void Configure(EntityTypeBuilder<WorkOrderTaskMaterial> builder)
    {
        builder.ToTable("work_order_task_materials");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.WorkOrderId).IsRequired();
        builder.Property(m => m.WorkOrderTaskId).IsRequired();

        builder.OwnsOne(m => m.Material, material =>
        {
            material.Property(p => p.MaterialId).HasColumnName("MaterialId").IsRequired();
            material.Property(p => p.Name).HasColumnName("MaterialName").HasMaxLength(200).IsRequired();
        });

        builder.OwnsOne(m => m.Quantity, q =>
            q.Property(p => p.Value).HasColumnName("Quantity").HasPrecision(18, 2).IsRequired());

        builder.HasIndex(m => m.WorkOrderTaskId);
    }
}

public sealed class WorkOrderTaskGeneralSupportConfiguration : IEntityTypeConfiguration<WorkOrderTaskGeneralSupport>
{
    public void Configure(EntityTypeBuilder<WorkOrderTaskGeneralSupport> builder)
    {
        builder.ToTable("work_order_task_general_supports");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.Property(g => g.WorkOrderId).IsRequired();
        builder.Property(g => g.WorkOrderTaskId).IsRequired();

        builder.OwnsOne(g => g.GeneralSupport, support =>
        {
            support.Property(p => p.GeneralSupportId).HasColumnName("GeneralSupportId").IsRequired();
            support.Property(p => p.Name).HasColumnName("GeneralSupportName").HasMaxLength(200).IsRequired();
        });

        builder.OwnsOne(g => g.Quantity, q =>
            q.Property(p => p.Value).HasColumnName("Quantity").HasPrecision(18, 2).IsRequired());

        builder.HasIndex(g => g.WorkOrderTaskId);
    }
}

public sealed class WorkOrderTaskAttachmentConfiguration : IEntityTypeConfiguration<WorkOrderTaskAttachment>
{
    public void Configure(EntityTypeBuilder<WorkOrderTaskAttachment> builder)
    {
        builder.ToTable("work_order_task_attachments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.WorkOrderId).IsRequired();
        builder.Property(a => a.WorkOrderTaskId).IsRequired();
        builder.Property(a => a.Kind).HasConversion<int>();
        builder.Property(a => a.StorageReference).HasMaxLength(500).IsRequired();
        builder.Property(a => a.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Size).IsRequired();

        builder.HasIndex(a => a.WorkOrderTaskId);
    }
}

public sealed class WorkOrderTimelineEntryConfiguration : IEntityTypeConfiguration<WorkOrderTimelineEntry>
{
    public void Configure(EntityTypeBuilder<WorkOrderTimelineEntry> builder)
    {
        builder.ToTable("work_order_timeline_entries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.WorkOrderId).IsRequired();
        builder.Property(e => e.EventType).HasConversion<int>();
        builder.Property(e => e.OccurredAtUtc).IsRequired();
        builder.Property(e => e.ActorUserId).IsRequired();
        builder.Property(e => e.ActorName).HasMaxLength(200);
        builder.Property(e => e.Details).HasMaxLength(1000);

        builder.HasIndex(e => new { e.WorkOrderId, e.OccurredAtUtc });
    }
}
