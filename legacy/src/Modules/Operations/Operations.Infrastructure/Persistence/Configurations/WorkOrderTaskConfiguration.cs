using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Operations.Domain.Entities;
using WorkOrderId = Operations.Domain.Aggregates.WorkOrder.WorkOrderId;

namespace Operations.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for <see cref="WorkOrderTask"/>: the unified replacement for
/// <c>WorkOrderEmployeeLine</c> + <c>CorrectiveAction</c>. Per-task line collections
/// (employees / tools / materials / general supports / attachments) are mapped via
/// dedicated configurations.
/// </summary>
public sealed class WorkOrderTaskConfiguration : IEntityTypeConfiguration<WorkOrderTask>
{
    public void Configure(EntityTypeBuilder<WorkOrderTask> builder)
    {
        builder.ToTable("WorkOrderTasks", "operations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.WorkOrderId)
            .HasConversion(v => v.Value, v => WorkOrderId.From(v))
            .IsRequired();

        builder.Property(x => x.TaskType).HasConversion<int>().IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.From).IsRequired();
        builder.Property(x => x.To).IsRequired();
        builder.Property(x => x.ReturnToRamp).IsRequired();

        builder.Ignore(nameof(WorkOrderTask.Employees));
        builder.Ignore(nameof(WorkOrderTask.Tools));
        builder.Ignore(nameof(WorkOrderTask.Materials));
        builder.Ignore(nameof(WorkOrderTask.GeneralSupports));
        builder.Ignore(nameof(WorkOrderTask.Attachments));

        builder.HasMany(typeof(WorkOrderTaskEmployee), "_employees")
            .WithOne()
            .HasForeignKey(nameof(WorkOrderTaskEmployee.TaskId))
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(typeof(WorkOrderTaskTool), "_tools")
            .WithOne()
            .HasForeignKey(nameof(WorkOrderTaskTool.TaskId))
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(typeof(WorkOrderTaskMaterial), "_materials")
            .WithOne()
            .HasForeignKey(nameof(WorkOrderTaskMaterial.TaskId))
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(typeof(WorkOrderTaskGeneralSupport), "_generalSupports")
            .WithOne()
            .HasForeignKey(nameof(WorkOrderTaskGeneralSupport.TaskId))
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(typeof(WorkOrderTaskAttachment), "_attachments")
            .WithOne()
            .HasForeignKey(nameof(WorkOrderTaskAttachment.TaskId))
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(nameof(WorkOrderTask.WorkOrderId));
    }
}

public sealed class WorkOrderTaskEmployeeConfiguration : IEntityTypeConfiguration<WorkOrderTaskEmployee>
{
    public void Configure(EntityTypeBuilder<WorkOrderTaskEmployee> builder)
    {
        builder.ToTable("WorkOrderTaskEmployees", "operations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TaskId).IsRequired();

        builder.OwnsOne(x => x.Employee, e =>
        {
            e.Property(p => p.EmployeeId).HasColumnName("EmployeeId").IsRequired();
            e.Property(p => p.FullName).HasColumnName("EmployeeFullName").HasMaxLength(200).IsRequired();
            e.OwnsOne(p => p.StationSnapshot, s =>
            {
                s.Property(p => p.StationId).HasColumnName("EmployeeStationId").IsRequired();
                s.Property(p => p.IataCode).HasColumnName("EmployeeStationIataCode").HasMaxLength(3).IsRequired();
                s.Property(p => p.Name).HasColumnName("EmployeeStationName").HasMaxLength(150).IsRequired();
            });
            e.OwnsOne(p => p.ManpowerTypeSnapshot, m =>
            {
                m.Property(p => p.ManpowerTypeId).HasColumnName("EmployeeManpowerTypeId").IsRequired();
                m.Property(p => p.Name).HasColumnName("EmployeeManpowerTypeName").HasMaxLength(150).IsRequired();
            });
        });
        builder.Navigation(x => x.Employee).IsRequired();

        builder.HasIndex(x => x.TaskId);
    }
}

public sealed class WorkOrderTaskToolConfiguration : IEntityTypeConfiguration<WorkOrderTaskTool>
{
    public void Configure(EntityTypeBuilder<WorkOrderTaskTool> builder)
    {
        builder.ToTable("WorkOrderTaskTools", "operations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TaskId).IsRequired();

        builder.OwnsOne(x => x.Tool, t =>
        {
            t.Property(p => p.ToolId).HasColumnName("ToolId").IsRequired();
            t.Property(p => p.Name).HasColumnName("ToolName").HasMaxLength(150).IsRequired();
        });
        builder.Navigation(x => x.Tool).IsRequired();

        builder.HasIndex(x => x.TaskId);
    }
}

public sealed class WorkOrderTaskMaterialConfiguration : IEntityTypeConfiguration<WorkOrderTaskMaterial>
{
    public void Configure(EntityTypeBuilder<WorkOrderTaskMaterial> builder)
    {
        builder.ToTable("WorkOrderTaskMaterials", "operations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TaskId).IsRequired();

        builder.OwnsOne(x => x.Material, m =>
        {
            m.Property(p => p.MaterialId).HasColumnName("MaterialId").IsRequired();
            m.Property(p => p.Name).HasColumnName("MaterialName").HasMaxLength(150).IsRequired();
        });
        builder.Navigation(x => x.Material).IsRequired();

        builder.HasIndex(x => x.TaskId);
    }
}

public sealed class WorkOrderTaskGeneralSupportConfiguration : IEntityTypeConfiguration<WorkOrderTaskGeneralSupport>
{
    public void Configure(EntityTypeBuilder<WorkOrderTaskGeneralSupport> builder)
    {
        builder.ToTable("WorkOrderTaskGeneralSupports", "operations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TaskId).IsRequired();

        builder.OwnsOne(x => x.GeneralSupport, g =>
        {
            g.Property(p => p.GeneralSupportId).HasColumnName("GeneralSupportId").IsRequired();
            g.Property(p => p.Name).HasColumnName("GeneralSupportName").HasMaxLength(150).IsRequired();
        });
        builder.Navigation(x => x.GeneralSupport).IsRequired();

        builder.HasIndex(x => x.TaskId);
    }
}

public sealed class WorkOrderTaskAttachmentConfiguration : IEntityTypeConfiguration<WorkOrderTaskAttachment>
{
    public void Configure(EntityTypeBuilder<WorkOrderTaskAttachment> builder)
    {
        builder.ToTable("WorkOrderTaskAttachments", "operations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TaskId).IsRequired();

        builder.Property(x => x.Kind).HasConversion<int>().IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.SizeBytes).IsRequired();
        builder.Property(x => x.CapturedAt).IsRequired();
        builder.Property(x => x.Bytes).HasColumnType("varbinary(max)").IsRequired();

        builder.HasIndex(x => x.TaskId);
    }
}
