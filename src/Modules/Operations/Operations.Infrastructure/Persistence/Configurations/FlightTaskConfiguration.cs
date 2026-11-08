using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MasterData.Contracts.Resources;
using Operations.Domain.Flights;

namespace Operations.Infrastructure.Persistence.Configurations;

public sealed class FlightTaskConfiguration : IEntityTypeConfiguration<FlightTask>
{
    public void Configure(EntityTypeBuilder<FlightTask> builder)
    {
        builder.ToTable("flight_tasks");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.HasIndex(t => t.FlightId);
        builder.Property(t => t.TaskType).IsRequired();
        builder.Property(t => t.EndedByUserId);
        builder.Property(t => t.CustomerReferenceNumber).HasMaxLength(200);
        builder.Property(t => t.Notes).HasMaxLength(2000);

        builder.OwnsOne(t => t.AtaChapter, chapter =>
        {
            chapter.Property(c => c.AtaChapterId).HasColumnName("AtaChapterId").IsRequired();
            chapter.Property(c => c.Code).HasColumnName("AtaChapterCode").HasMaxLength(20).IsRequired();
            chapter.Property(c => c.Title).HasColumnName("AtaChapterTitle").HasMaxLength(200).IsRequired();
        });
        builder.Navigation(t => t.AtaChapter).IsRequired(false);

        builder.OwnsOne(t => t.Window, window =>
        {
            window.Property(w => w.FromUtc).HasColumnName("FromUtc").IsRequired();
            window.Property(w => w.ToUtc).HasColumnName("ToUtc");
        });
        builder.Navigation(t => t.Window).IsRequired();

        builder.HasMany(t => t.Tools).WithOne().HasForeignKey(t => t.FlightTaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.Materials).WithOne().HasForeignKey(m => m.FlightTaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.GeneralSupports).WithOne().HasForeignKey(g => g.FlightTaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.Performers).WithOne().HasForeignKey(p => p.FlightTaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.Attachments).WithOne().HasForeignKey(a => a.FlightTaskId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class FlightTaskToolConfiguration : IEntityTypeConfiguration<FlightTaskTool>
{
    public void Configure(EntityTypeBuilder<FlightTaskTool> builder)
    {
        builder.ToTable("flight_task_tools", table => table.HasCheckConstraint(
            "CK_flight_task_tools_ResourceUsage",
            "([CalculationType] = 0 AND [Quantity] IS NOT NULL AND [Quantity] > 0 AND [FromUtc] IS NULL AND [ToUtc] IS NULL) OR ([CalculationType] = 1 AND [Quantity] IS NULL AND [FromUtc] IS NOT NULL AND ([ToUtc] IS NULL OR [ToUtc] >= [FromUtc]))"));
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.HasIndex(t => t.FlightTaskId);
        builder.Property(t => t.Notes).HasMaxLength(2000);
        // EF-only index property shares the snapshot column; Tool owns the resource identity.
        builder.Property<Guid>("ToolId").HasColumnName("ToolId").IsRequired();
        builder.HasIndex(nameof(FlightTaskTool.FlightTaskId), "ToolId").IsUnique();

        builder.OwnsOne(t => t.Tool, tool =>
        {
            tool.Property(t => t.ToolId).HasColumnName("ToolId").IsRequired();
            tool.Property(t => t.Name).HasColumnName("ToolName").HasMaxLength(200).IsRequired();
            tool.Property(t => t.CalculationType).HasColumnName("CalculationType").HasConversion<int>()
                .HasDefaultValue(ResourceCalculationType.Duration).ValueGeneratedNever().IsRequired();
        });
        builder.Navigation(t => t.Tool).IsRequired();

        builder.OwnsOne(t => t.Usage, usage =>
        {
            usage.Property(u => u.Quantity).HasColumnName("Quantity").HasPrecision(18, 2);
            usage.Property(u => u.FromUtc).HasColumnName("FromUtc");
            usage.Property(u => u.ToUtc).HasColumnName("ToUtc");
            usage.Ignore(u => u.CalculationType);
        });
        builder.Navigation(t => t.Usage).IsRequired();
    }
}

public sealed class FlightTaskMaterialConfiguration : IEntityTypeConfiguration<FlightTaskMaterial>
{
    public void Configure(EntityTypeBuilder<FlightTaskMaterial> builder)
    {
        builder.ToTable("flight_task_materials", table => table.HasCheckConstraint(
            "CK_flight_task_materials_ResourceUsage",
            "([CalculationType] = 0 AND [Quantity] IS NOT NULL AND [Quantity] > 0 AND [FromUtc] IS NULL AND [ToUtc] IS NULL) OR ([CalculationType] = 1 AND [Quantity] IS NULL AND [FromUtc] IS NOT NULL AND ([ToUtc] IS NULL OR [ToUtc] >= [FromUtc]))"));
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.HasIndex(m => m.FlightTaskId);
        builder.Property(m => m.Notes).HasMaxLength(2000);
        // EF-only index property shares the snapshot column; Material owns the resource identity.
        builder.Property<Guid>("MaterialId").HasColumnName("MaterialId").IsRequired();
        builder.HasIndex(nameof(FlightTaskMaterial.FlightTaskId), "MaterialId").IsUnique();

        builder.OwnsOne(m => m.Material, material =>
        {
            material.Property(m => m.MaterialId).HasColumnName("MaterialId").IsRequired();
            material.Property(m => m.Name).HasColumnName("MaterialName").HasMaxLength(200).IsRequired();
            material.Property(m => m.CalculationType).HasColumnName("CalculationType").HasConversion<int>()
                .HasDefaultValue(ResourceCalculationType.Quantity).ValueGeneratedNever().IsRequired();
        });
        builder.Navigation(m => m.Material).IsRequired();

        builder.OwnsOne(m => m.Usage, usage =>
        {
            usage.Property(u => u.Quantity).HasColumnName("Quantity").HasPrecision(18, 2);
            usage.Property(u => u.FromUtc).HasColumnName("FromUtc");
            usage.Property(u => u.ToUtc).HasColumnName("ToUtc");
            usage.Ignore(u => u.CalculationType);
        });
        builder.Navigation(m => m.Usage).IsRequired();
    }
}

public sealed class FlightTaskGeneralSupportConfiguration : IEntityTypeConfiguration<FlightTaskGeneralSupport>
{
    public void Configure(EntityTypeBuilder<FlightTaskGeneralSupport> builder)
    {
        builder.ToTable("flight_task_general_supports", table => table.HasCheckConstraint(
            "CK_flight_task_general_supports_ResourceUsage",
            "([CalculationType] = 0 AND [Quantity] IS NOT NULL AND [Quantity] > 0 AND [FromUtc] IS NULL AND [ToUtc] IS NULL) OR ([CalculationType] = 1 AND [Quantity] IS NULL AND [FromUtc] IS NOT NULL AND ([ToUtc] IS NULL OR [ToUtc] >= [FromUtc]))"));
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.HasIndex(g => g.FlightTaskId);
        builder.Property(g => g.Notes).HasMaxLength(2000);
        // EF-only index property shares the snapshot column; GeneralSupport owns the resource identity.
        builder.Property<Guid>("GeneralSupportId").HasColumnName("GeneralSupportId").IsRequired();
        builder.HasIndex(nameof(FlightTaskGeneralSupport.FlightTaskId), "GeneralSupportId").IsUnique();

        builder.OwnsOne(g => g.GeneralSupport, support =>
        {
            support.Property(g => g.GeneralSupportId).HasColumnName("GeneralSupportId").IsRequired();
            support.Property(g => g.Name).HasColumnName("GeneralSupportName").HasMaxLength(200).IsRequired();
            support.Property(g => g.CalculationType).HasColumnName("CalculationType").HasConversion<int>()
                .HasDefaultValue(ResourceCalculationType.Quantity).ValueGeneratedNever().IsRequired();
        });
        builder.Navigation(g => g.GeneralSupport).IsRequired();

        builder.OwnsOne(g => g.Usage, usage =>
        {
            usage.Property(u => u.Quantity).HasColumnName("Quantity").HasPrecision(18, 2);
            usage.Property(u => u.FromUtc).HasColumnName("FromUtc");
            usage.Property(u => u.ToUtc).HasColumnName("ToUtc");
            usage.Ignore(u => u.CalculationType);
        });
        builder.Navigation(g => g.Usage).IsRequired();
    }
}

public sealed class FlightTaskPerformerConfiguration : IEntityTypeConfiguration<FlightTaskPerformer>
{
    public void Configure(EntityTypeBuilder<FlightTaskPerformer> builder)
    {
        builder.ToTable("flight_task_performers");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.HasIndex(p => p.FlightTaskId);
        builder.Property(p => p.Notes).HasMaxLength(2000);
        // EF-only index property shares the snapshot column; Employee owns the staff identity.
        builder.Property<Guid>("StaffMemberId").HasColumnName("StaffMemberId").IsRequired();
        builder.HasIndex(nameof(FlightTaskPerformer.FlightTaskId), "StaffMemberId").IsUnique();

        builder.OwnsOne(p => p.Employee, employee =>
        {
            employee.Property(e => e.StaffMemberId).HasColumnName("StaffMemberId").IsRequired();
            employee.Property(e => e.FullName).HasColumnName("StaffFullName").HasMaxLength(200).IsRequired();
            employee.Property(e => e.EmployeeId).HasColumnName("StaffEmployeeId").HasMaxLength(50).IsRequired();
        });
        builder.Navigation(p => p.Employee).IsRequired();

        builder.OwnsOne(p => p.Window, window =>
        {
            window.Property(w => w.FromUtc).HasColumnName("FromUtc").IsRequired();
            window.Property(w => w.ToUtc).HasColumnName("ToUtc");
        });
        builder.Navigation(p => p.Window).IsRequired();
    }
}

public sealed class FlightTaskAttachmentConfiguration : IEntityTypeConfiguration<FlightTaskAttachment>
{
    public void Configure(EntityTypeBuilder<FlightTaskAttachment> builder)
    {
        builder.ToTable("flight_task_attachments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.HasIndex(a => a.FlightTaskId);

        builder.OwnsOne(a => a.Details, details =>
        {
            details.Property(d => d.Kind).HasColumnName("Kind").HasConversion<int>().IsRequired();
            details.Property(d => d.FileName).HasColumnName("FileName").HasMaxLength(255).IsRequired();
            details.Property(d => d.StorageKey).HasColumnName("StorageKey").HasMaxLength(500).IsRequired();
            details.Property(d => d.ContentType).HasColumnName("ContentType").HasMaxLength(100).IsRequired();
            details.Property(d => d.SizeBytes).HasColumnName("SizeBytes").IsRequired();
        });
        builder.Navigation(a => a.Details).IsRequired();
    }
}
