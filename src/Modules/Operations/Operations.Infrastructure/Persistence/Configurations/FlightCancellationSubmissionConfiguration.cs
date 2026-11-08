using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Operations.Domain.Flights;

namespace Operations.Infrastructure.Persistence.Configurations;

public sealed class FlightCancellationSubmissionConfiguration : IEntityTypeConfiguration<FlightCancellationSubmission>
{
    public void Configure(EntityTypeBuilder<FlightCancellationSubmission> builder)
    {
        builder.ToTable("flight_cancellation_submissions");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        // Share the snapshot's column for the per-flight index without duplicating domain state.
        builder.Property<Guid>("SubmittedByStaffMemberId").HasColumnName("SubmittedByStaffMemberId").IsRequired();

        builder.OwnsOne(c => c.SubmittedByEmployee, employee =>
        {
            employee.Property(e => e.StaffMemberId).HasColumnName("SubmittedByStaffMemberId").IsRequired();
            employee.Property(e => e.FullName).HasColumnName("SubmittedByStaffFullName").HasMaxLength(200).IsRequired();
            employee.Property(e => e.EmployeeId).HasColumnName("SubmittedByStaffEmployeeId").HasMaxLength(50).IsRequired();
        });
        builder.Navigation(c => c.SubmittedByEmployee).IsRequired();

        builder.OwnsOne(c => c.Details, details =>
        {
            details.Property(d => d.CanceledAtUtc).HasColumnName("CanceledAtUtc").IsRequired();
            details.Property(d => d.Remarks).HasColumnName("Remarks");
        });
        builder.Navigation(c => c.Details).IsRequired();

        builder.HasIndex(nameof(FlightCancellationSubmission.FlightId), "SubmittedByStaffMemberId").IsUnique();
        builder.HasIndex(c => c.FlightId).IsUnique().HasFilter("[IsAccepted] = 1");
    }
}
