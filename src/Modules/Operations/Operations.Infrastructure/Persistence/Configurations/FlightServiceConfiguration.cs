using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Operations.Domain.Flights;

namespace Operations.Infrastructure.Persistence.Configurations;

public sealed class FlightServiceConfiguration : IEntityTypeConfiguration<FlightService>
{
    public void Configure(EntityTypeBuilder<FlightService> builder)
    {
        builder.ToTable("flight_services");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.HasIndex(s => s.FlightId);
        builder.Property(s => s.ReturnToRampId);
        builder.HasIndex(s => new { s.ReturnToRampId, s.FlightId });
        builder.Property(s => s.EndedByUserId);
        builder.Property(s => s.CustomerReferenceNumber).HasMaxLength(200);
        builder.Property(s => s.Notes).HasMaxLength(2000);

        builder.OwnsOne(s => s.Service, service =>
        {
            service.Property(s => s.ServiceId).HasColumnName("ServiceId").IsRequired();
            service.Property(s => s.Name).HasColumnName("ServiceName").HasMaxLength(200).IsRequired();
        });
        builder.Navigation(s => s.Service).IsRequired();

        builder.OwnsOne(s => s.Window, window =>
        {
            window.Property(w => w.FromUtc).HasColumnName("FromUtc").IsRequired();
            window.Property(w => w.ToUtc).HasColumnName("ToUtc");
        });
        builder.Navigation(s => s.Window).IsRequired();

        builder.HasMany(s => s.Performers).WithOne().HasForeignKey(p => p.FlightServiceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(s => s.Attachments).WithOne().HasForeignKey(a => a.FlightServiceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class FlightServicePerformerConfiguration : IEntityTypeConfiguration<FlightServicePerformer>
{
    public void Configure(EntityTypeBuilder<FlightServicePerformer> builder)
    {
        builder.ToTable("flight_service_performers");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.HasIndex(p => p.FlightServiceId);
        // EF-only index property shares the snapshot column; Employee owns the staff identity.
        builder.Property<Guid>("StaffMemberId").HasColumnName("StaffMemberId").IsRequired();
        builder.HasIndex(nameof(FlightServicePerformer.FlightServiceId), "StaffMemberId").IsUnique();

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

public sealed class FlightServiceAttachmentConfiguration : IEntityTypeConfiguration<FlightServiceAttachment>
{
    public void Configure(EntityTypeBuilder<FlightServiceAttachment> builder)
    {
        builder.ToTable("flight_service_attachments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.HasIndex(a => a.FlightServiceId);

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
