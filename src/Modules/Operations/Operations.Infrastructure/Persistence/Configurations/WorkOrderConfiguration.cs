using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Operations.Domain.WorkOrders;

namespace Operations.Infrastructure.Persistence.Configurations;

public sealed class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    public void Configure(EntityTypeBuilder<WorkOrder> builder)
    {
        builder.ToTable("work_orders");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.FlightId).IsRequired();
        builder.Property(w => w.Type).HasConversion<int>();
        builder.Property(w => w.Status).HasConversion<int>();
        builder.Property(w => w.AircraftTailNumber).HasMaxLength(20);
        builder.Property(w => w.Remarks).HasMaxLength(2000);
        builder.Property(w => w.CustomerSignatureReference).HasMaxLength(400);
        builder.Property(w => w.CreatedAtUtc).IsRequired();
        builder.Property(w => w.RowVersion).IsRowVersion();

        builder.Property(w => w.OwnerStaffMemberId);

        builder.OwnsOne(w => w.Owner, s =>
        {
            s.Property(x => x.StaffMemberId).HasColumnName("OwnerSnapshotStaffMemberId");
            s.Property(x => x.FullName).HasColumnName("OwnerFullName").HasMaxLength(200);
            s.Property(x => x.EmployeeId).HasColumnName("OwnerEmployeeId").HasMaxLength(50);
        });

        builder.OwnsOne(w => w.Number, n =>
            n.Property(p => p.Value).HasColumnName("WorkOrderNumber").HasMaxLength(30));

        builder.OwnsOne(w => w.FlightNumber, n =>
            n.Property(p => p.Value).HasColumnName("FlightNumber").HasMaxLength(12).IsRequired());

        builder.OwnsOne(w => w.Schedule, s =>
        {
            s.Property(p => p.Sta).HasColumnName("ScheduledArrivalUtc").IsRequired();
            s.Property(p => p.Std).HasColumnName("ScheduledDepartureUtc").IsRequired();
        });

        builder.OwnsOne(w => w.Actuals, a =>
        {
            a.Property(p => p.Ata).HasColumnName("ActualArrivalUtc");
            a.Property(p => p.Atd).HasColumnName("ActualDepartureUtc");
        });

        builder.OwnsOne(w => w.Cancellation, c =>
        {
            c.Property(p => p.CanceledByUserId).HasColumnName("CanceledByUserId");
            c.Property(p => p.CanceledAtUtc).HasColumnName("CanceledAtUtc");
            c.Property(p => p.Reason).HasColumnName("CancellationReason").HasMaxLength(1000);
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

        builder.OwnsOne(w => w.AircraftType, a =>
        {
            a.Property(p => p.AircraftTypeId).HasColumnName("AircraftTypeId");
            a.Property(p => p.Manufacturer).HasColumnName("AircraftManufacturer").HasMaxLength(100);
            a.Property(p => p.Model).HasColumnName("AircraftModel").HasMaxLength(50);
        });

        builder.HasMany(w => w.ServiceLines).WithOne().HasForeignKey(l => l.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(w => w.Tasks).WithOne().HasForeignKey(t => t.WorkOrderId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(w => w.FlightId);
        builder.HasIndex(w => w.Status);
        builder.HasIndex(w => w.OwnerStaffMemberId);

        builder.Ignore(w => w.IsCancellation);
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
        builder.Property(l => l.WorkOrderId).IsRequired();
        builder.Property(l => l.Origin).HasConversion<int>();
        builder.Property(l => l.Description).HasMaxLength(1000);

        builder.OwnsOne(l => l.Service, s =>
        {
            s.Property(x => x.ServiceId).HasColumnName("ServiceId").IsRequired();
            s.Property(x => x.Name).HasColumnName("ServiceName").HasMaxLength(200).IsRequired();
        });

        builder.OwnsOne(l => l.Window, w =>
        {
            w.Property(p => p.From).HasColumnName("FromUtc").IsRequired();
            w.Property(p => p.To).HasColumnName("ToUtc").IsRequired();
            w.Ignore(p => p.Duration);
        });

        builder.HasMany(l => l.Employees).WithOne().HasForeignKey(e => e.ServiceLineId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(l => l.WorkOrderId);
    }
}

public sealed class WorkOrderServiceLineEmployeeConfiguration : IEntityTypeConfiguration<WorkOrderServiceLineEmployee>
{
    public void Configure(EntityTypeBuilder<WorkOrderServiceLineEmployee> builder)
    {
        builder.ToTable("work_order_service_line_employees");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ServiceLineId).IsRequired();

        builder.OwnsOne(e => e.Employee, s =>
        {
            s.Property(x => x.StaffMemberId).HasColumnName("StaffMemberId").IsRequired();
            s.Property(x => x.FullName).HasColumnName("StaffFullName").HasMaxLength(200).IsRequired();
            s.Property(x => x.EmployeeId).HasColumnName("StaffEmployeeId").HasMaxLength(50).IsRequired();
        });

        builder.HasIndex(e => e.ServiceLineId);
    }
}

public sealed class WorkOrderTaskConfiguration : IEntityTypeConfiguration<WorkOrderTask>
{
    public void Configure(EntityTypeBuilder<WorkOrderTask> builder)
    {
        builder.ToTable("work_order_tasks");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.WorkOrderId).IsRequired();
        builder.Property(t => t.TaskType).HasConversion<int>();
        builder.Property(t => t.Description).HasMaxLength(1000);

        builder.OwnsOne(t => t.Window, w =>
        {
            w.Property(p => p.From).HasColumnName("FromUtc").IsRequired();
            w.Property(p => p.To).HasColumnName("ToUtc").IsRequired();
            w.Ignore(p => p.Duration);
        });

        builder.HasMany(t => t.Employees).WithOne().HasForeignKey(e => e.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.Tools).WithOne().HasForeignKey(e => e.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.Materials).WithOne().HasForeignKey(e => e.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.GeneralSupports).WithOne().HasForeignKey(e => e.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.Attachments).WithOne().HasForeignKey(e => e.TaskId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.WorkOrderId);
    }
}

public sealed class WorkOrderTaskEmployeeConfiguration : IEntityTypeConfiguration<WorkOrderTaskEmployee>
{
    public void Configure(EntityTypeBuilder<WorkOrderTaskEmployee> builder)
    {
        builder.ToTable("work_order_task_employees");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TaskId).IsRequired();

        builder.OwnsOne(e => e.Employee, s =>
        {
            s.Property(x => x.StaffMemberId).HasColumnName("StaffMemberId").IsRequired();
            s.Property(x => x.FullName).HasColumnName("StaffFullName").HasMaxLength(200).IsRequired();
            s.Property(x => x.EmployeeId).HasColumnName("StaffEmployeeId").HasMaxLength(50).IsRequired();
        });

        builder.HasIndex(e => e.TaskId);
    }
}

public sealed class WorkOrderTaskToolConfiguration : IEntityTypeConfiguration<WorkOrderTaskTool>
{
    public void Configure(EntityTypeBuilder<WorkOrderTaskTool> builder)
    {
        builder.ToTable("work_order_task_tools");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TaskId).IsRequired();

        builder.OwnsOne(e => e.Tool, s =>
        {
            s.Property(x => x.ToolId).HasColumnName("ToolId").IsRequired();
            s.Property(x => x.Name).HasColumnName("ToolName").HasMaxLength(200).IsRequired();
        });
        builder.OwnsOne(e => e.Quantity, q =>
            q.Property(p => p.Value).HasColumnName("Quantity").HasPrecision(18, 3).IsRequired());

        builder.HasIndex(e => e.TaskId);
    }
}

public sealed class WorkOrderTaskMaterialConfiguration : IEntityTypeConfiguration<WorkOrderTaskMaterial>
{
    public void Configure(EntityTypeBuilder<WorkOrderTaskMaterial> builder)
    {
        builder.ToTable("work_order_task_materials");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TaskId).IsRequired();

        builder.OwnsOne(e => e.Material, s =>
        {
            s.Property(x => x.MaterialId).HasColumnName("MaterialId").IsRequired();
            s.Property(x => x.Name).HasColumnName("MaterialName").HasMaxLength(200).IsRequired();
        });
        builder.OwnsOne(e => e.Quantity, q =>
            q.Property(p => p.Value).HasColumnName("Quantity").HasPrecision(18, 3).IsRequired());

        builder.HasIndex(e => e.TaskId);
    }
}

public sealed class WorkOrderTaskGeneralSupportConfiguration : IEntityTypeConfiguration<WorkOrderTaskGeneralSupport>
{
    public void Configure(EntityTypeBuilder<WorkOrderTaskGeneralSupport> builder)
    {
        builder.ToTable("work_order_task_general_supports");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TaskId).IsRequired();

        builder.OwnsOne(e => e.GeneralSupport, s =>
        {
            s.Property(x => x.GeneralSupportId).HasColumnName("GeneralSupportId").IsRequired();
            s.Property(x => x.Name).HasColumnName("GeneralSupportName").HasMaxLength(200).IsRequired();
        });
        builder.OwnsOne(e => e.Quantity, q =>
            q.Property(p => p.Value).HasColumnName("Quantity").HasPrecision(18, 3).IsRequired());

        builder.HasIndex(e => e.TaskId);
    }
}

public sealed class WorkOrderTaskAttachmentConfiguration : IEntityTypeConfiguration<WorkOrderTaskAttachment>
{
    public void Configure(EntityTypeBuilder<WorkOrderTaskAttachment> builder)
    {
        builder.ToTable("work_order_task_attachments");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TaskId).IsRequired();
        builder.Property(e => e.Kind).HasConversion<int>();
        builder.Property(e => e.ContentType).HasMaxLength(120).IsRequired();
        builder.Property(e => e.FileName).HasMaxLength(260).IsRequired();
        builder.Property(e => e.StorageReference).HasMaxLength(400).IsRequired();
        builder.Property(e => e.SizeBytes).IsRequired();
        builder.Property(e => e.CapturedAtUtc).IsRequired();

        builder.HasIndex(e => e.TaskId);
    }
}
